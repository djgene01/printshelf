// PrintShelf 3D preview — local three.js only, no CDN (NFR-08).
// Lifecycle: createViewer() -> one ResizeObserver + RAF loop; disposeViewer()
// always releases geometry, material, renderer and the WebGL context (R-03).
//
// Thumbnails are rendered by the browser (U-02). The same studio setup is used
// for the live viewport, the one-off thumbnail and the batch preview generator,
// so a saved PNG looks like what the user sees.

import * as THREE from "../vendor/three/three.module.js";
import { STLLoader } from "../vendor/three/STLLoader.js";
import { OrbitControls } from "../vendor/three/OrbitControls.js";

const viewers = new Map();
const loader = new STLLoader();

// A lighter studio backdrop keeps pale models readable on the dark UI; a
// near-black viewport made the whole panel look unlit on dim displays.
function makeBackdropTexture() {
    const canvas = document.createElement("canvas");
    canvas.width = 2;
    canvas.height = 512;
    const ctx = canvas.getContext("2d");
    const gradient = ctx.createLinearGradient(0, 0, 0, 512);
    gradient.addColorStop(0, "#5b6b80");
    gradient.addColorStop(0.55, "#334155");
    gradient.addColorStop(1, "#0f172a");
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, 2, 512);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    return texture;
}

function addStudioLights(scene) {
    scene.add(new THREE.HemisphereLight(0xffffff, 0x64748b, 2.2));

    const key = new THREE.DirectionalLight(0xffffff, 3.4);
    key.position.set(-1.2, 1.8, 1.0);
    key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024);
    key.shadow.bias = -0.0002;
    scene.add(key);

    const fill = new THREE.DirectionalLight(0xbfdbfe, 1.7);
    fill.position.set(1, 0.2, -0.8);
    scene.add(fill);

    const rim = new THREE.DirectionalLight(0xffffff, 2.2);
    rim.position.set(0.2, 0.8, -1.4);
    scene.add(rim);

    scene.add(new THREE.AmbientLight(0xffffff, 0.45));
    return { key, fill, rim };
}

// Scale the key light and its shadow frustum to the model so small and large
// STLs get the same soft contact shadow instead of clipping or acne.
function fitStudio(lights, maxDim) {
    const { key } = lights;
    key.position.set(-maxDim * 1.6, maxDim * 2.1, maxDim * 1.35);
    key.shadow.normalBias = maxDim * 0.006;
    key.shadow.camera.left = -maxDim * 2.4;
    key.shadow.camera.right = maxDim * 2.4;
    key.shadow.camera.top = maxDim * 2.4;
    key.shadow.camera.bottom = -maxDim * 2.4;
    key.shadow.camera.near = Math.max(maxDim * 0.05, 0.01);
    key.shadow.camera.far = maxDim * 9;
    key.shadow.camera.updateProjectionMatrix();
}

// Transparent catcher receives the shadow while the gradient/grid stay visible.
function addShadowCatcher(scene, maxDim, size) {
    const plane = new THREE.Mesh(
        new THREE.PlaneGeometry(maxDim * 7, maxDim * 7),
        new THREE.ShadowMaterial({ color: 0x000000, opacity: 0.42, depthWrite: false })
    );
    plane.rotation.x = -Math.PI / 2;
    plane.position.y = -size.y / 2 - maxDim * 0.004;
    plane.receiveShadow = true;
    scene.add(plane);
    return plane;
}

function configureRenderer(renderer) {
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.18;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
}

function makeMaterial(color) {
    return new THREE.MeshStandardMaterial({
        color: new THREE.Color(color || "#e7edf5"),
        metalness: 0.04,
        roughness: 0.52,
        flatShading: false,
        side: THREE.DoubleSide // inverted-winding STLs still render
    });
}

// Fit a persisted mesh into a camera; shared by live and offscreen renders.
function fitCamera(camera, maxDim) {
    const distance = (maxDim / (2 * Math.tan((camera.fov * Math.PI / 180) / 2))) * 1.45;
    const direction = new THREE.Vector3(0.9, 0.62, 0.9).normalize();
    camera.position.copy(direction.multiplyScalar(distance));
    camera.lookAt(0, 0, 0);
    camera.near = Math.max(maxDim / 1000, 0.01);
    camera.far = Math.max(maxDim * 100, 100);
    camera.updateProjectionMatrix();
}

function normalizeGeometry(geometry) {
    // STL is Z-up; three.js is Y-up. Rotate before measuring so the model
    // stands upright and the camera fit uses real extents.
    geometry.rotateX(-Math.PI / 2);
    geometry.computeBoundingBox();
    const box = geometry.boundingBox ?? new THREE.Box3(new THREE.Vector3(-1, -1, -1), new THREE.Vector3(1, 1, 1));
    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());
    const maxDim = Math.max(size.x, size.y, size.z) || 1;
    geometry.translate(-center.x, -center.y, -center.z);

    // Many exporters write zero-length facet normals (the file is still valid).
    // three.js would light those faces as pure black, so rebuild normals from
    // the triangle positions instead of trusting the STL's stored normals.
    geometry.deleteAttribute("normal");
    geometry.computeVertexNormals();
    return { maxDim, size };
}

function addGrid(scene, maxDim, size) {
    const grid = new THREE.GridHelper(maxDim * 4, 20, 0x64748b, 0x475569);
    grid.position.y = -size.y / 2 - maxDim * 0.01;
    grid.material.transparent = true;
    grid.material.opacity = 0.45;
    scene.add(grid);
    return grid;
}

export function createViewer(elementId, url, modelId, hasThumb, dotNetRef, materialColor) {
    disposeViewer(elementId);

    const container = document.getElementById(elementId);
    if (!container) return false;

    let renderer;
    try {
        renderer = new THREE.WebGLRenderer({
            antialias: true,
            alpha: false,
            preserveDrawingBuffer: true // required for the one-off thumbnail capture
        });
    } catch (err) {
        notifyError(dotNetRef, "WebGL is not available in this browser: " + err.message);
        return false;
    }

    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setSize(container.clientWidth || 640, container.clientHeight || 420, false);
    configureRenderer(renderer);
    renderer.domElement.style.width = "100%";
    renderer.domElement.style.height = "100%";
    renderer.domElement.style.display = "block";
    renderer.domElement.setAttribute("aria-label", "Interactive 3D preview");
    container.appendChild(renderer.domElement);

    const scene = new THREE.Scene();
    const backdrop = makeBackdropTexture();
    scene.background = backdrop;
    const lights = addStudioLights(scene);

    const camera = new THREE.PerspectiveCamera(45, (container.clientWidth || 640) / (container.clientHeight || 420), 0.1, 10000);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.rotateSpeed = 0.8;
    controls.zoomSpeed = 0.9;
    controls.panSpeed = 0.8;

    const instance = {
        elementId,
        container,
        renderer,
        scene,
        camera,
        controls,
        mesh: null,
        grid: null,
        shadowPlane: null,
        lights,
        backdrop,
        raf: 0,
        resizeObserver: null,
        disposed: false,
        fit: null,
        captured: hasThumb === true,
        modelId,
        dotNetRef,
        maxDim: 1
    };
    viewers.set(elementId, instance);

    const animate = () => {
        if (instance.disposed) return;
        instance.raf = requestAnimationFrame(animate);
        controls.update();
        renderer.render(scene, camera);
    };
    animate();

    loader.load(url, (geometry) => {
        if (instance.disposed) {
            geometry.dispose();
            return;
        }

        const { maxDim, size } = normalizeGeometry(geometry);
        instance.maxDim = maxDim;
        const mesh = new THREE.Mesh(geometry, makeMaterial(materialColor));
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        scene.add(mesh);
        instance.mesh = mesh;
        instance.grid = addGrid(scene, maxDim, size);
        fitStudio(lights, maxDim);
        instance.shadowPlane = addShadowCatcher(scene, maxDim, size);

        instance.fit = () => {
            fitCamera(camera, maxDim);
            controls.target.set(0, 0, 0);
            controls.minDistance = maxDim * 0.05;
            controls.maxDistance = maxDim * 20;
            camera.updateProjectionMatrix();
            controls.update();
            renderer.render(scene, camera);
        };
        // The light and mesh are static while the camera orbits, so the shadow
        // map only needs one render; this keeps the 100k preview interactive.
        renderer.shadowMap.autoUpdate = false;
        renderer.shadowMap.needsUpdate = true;
        instance.fit();

        dotNetRef?.invokeMethodAsync("OnViewerReady");

        if (!instance.captured) {
            instance.captured = true;
            // One frame with the fitted camera, then POST the PNG once.
            requestAnimationFrame(() => captureThumbnail(instance));
        }
    }, undefined, (err) => {
        notifyError(dotNetRef, "Could not load the STL preview" + (err?.message ? ": " + err.message : "."));
    });

    const resize = () => {
        if (instance.disposed) return;
        const width = container.clientWidth || 640;
        const height = container.clientHeight || 420;
        camera.aspect = width / height;
        camera.updateProjectionMatrix();
        renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        renderer.setSize(width, height, false);
    };
    instance.resizeObserver = new ResizeObserver(resize);
    instance.resizeObserver.observe(container);

    return true;
}

export function resetViewer(elementId) {
    const instance = viewers.get(elementId);
    if (!instance || instance.disposed || !instance.fit) return;
    instance.fit();
}

export async function recaptureThumbnail(elementId) {
    const instance = viewers.get(elementId);
    if (!instance || instance.disposed || !instance.mesh) return false;
    return await captureThumbnail(instance);
}

// Download a high-resolution PNG rendered from the current scene/camera.
export async function savePng(elementId, filename) {
    const instance = viewers.get(elementId);
    if (!instance || instance.disposed || !instance.mesh) return false;

    const width = 1600;
    const height = 1200;
    let renderer;
    try {
        renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true, alpha: false });
    } catch (err) {
        console.warn("PrintShelf: PNG export renderer failed", err);
        return false;
    }

    try {
        renderer.setPixelRatio(1);
        renderer.setSize(width, height, false);
        configureRenderer(renderer);
        const camera = instance.camera.clone();
        camera.aspect = width / height;
        camera.updateProjectionMatrix();
        renderer.render(instance.scene, camera);

        const blob = await new Promise((resolve) => renderer.domElement.toBlob(resolve, "image/png"));
        if (!blob) return false;

        const objectUrl = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = objectUrl;
        link.download = filename || "printshelf-preview.png";
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(objectUrl), 4000);
        return true;
    } finally {
        renderer.dispose();
        renderer.forceContextLoss?.();
    }
}

// Batch render STLs to PNG thumbnails without opening each detail page.
// Items: [{ id, name }]; returns { ok, failed }.
export async function generatePreviews(items, dotNetRef, progressOffset = 0, grandTotal = items.length) {
    let ok = 0;
    let failed = 0;
    const total = items.length;
    const stage = createPreviewStage(480, 360);

    try {
        for (let i = 0; i < total; i++) {
            const item = items[i];
            try {
                const blob = await renderOnStage(stage, `/files/${item.id}`, item.color);
                if (!blob) throw new Error("render returned no image");

                const response = await fetch(`/api/models/${item.id}/thumbnail`, {
                    method: "POST",
                    headers: { "Content-Type": "image/png" },
                    body: blob
                });
                if (!response.ok) throw new Error(`POST ${response.status}`);
                ok++;
            } catch (err) {
                failed++;
                console.warn("PrintShelf: preview generation failed for", item.name, err);
            }

            const done = i + 1;
            if (done === total || done % 10 === 0) {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync("OnPreviewProgress", done + progressOffset, grandTotal, item.name, ok, failed).catch(() => {});
                }
            }
        }
    } finally {
        disposePreviewStage(stage);
    }

    return [ok, failed];
}

// One WebGL context is reused for the whole batch. Creating a renderer per
// model exhausts Chromium's context pool on large libraries.
function createPreviewStage(width, height) {
    const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true, alpha: false });
    renderer.setPixelRatio(1);
    renderer.setSize(width, height, false);
    configureRenderer(renderer);

    const backdrop = makeBackdropTexture();
    const scene = new THREE.Scene();
    scene.background = backdrop;
    const lights = addStudioLights(scene);
    const camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 10000);
    const stage = { renderer, backdrop, scene, lights, camera };
    // Thumbnails are 480×360; a 512 shadow map is plenty and keeps the
    // 1,000-model batch fast.
    stage.lights.key.shadow.mapSize.set(512, 512);
    return stage;
}

async function renderOnStage(stage, url, materialColor) {
    let mesh;
    let grid;
    let shadowPlane;
    try {
        const geometry = await loader.loadAsync(url);
        const { maxDim, size } = normalizeGeometry(geometry);
        mesh = new THREE.Mesh(geometry, makeMaterial(materialColor));
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        stage.scene.add(mesh);
        fitStudio(stage.lights, maxDim);
        grid = addGrid(stage.scene, maxDim, size);
        shadowPlane = addShadowCatcher(stage.scene, maxDim, size);

        fitCamera(stage.camera, maxDim);
        stage.renderer.render(stage.scene, stage.camera);
        return await new Promise((resolve) => stage.renderer.domElement.toBlob(resolve, "image/png"));
    } finally {
        for (const object of [mesh, grid, shadowPlane]) {
            if (!object) continue;
            stage.scene.remove(object);
            object.geometry?.dispose();
            object.material?.dispose();
        }
    }
}

function disposePreviewStage(stage) {
    stage.renderer.dispose();
    stage.renderer.forceContextLoss?.();
    stage.backdrop.dispose();
}

async function renderPreviewBlob(url, width, height, materialColor) {
    const stage = createPreviewStage(width, height);
    try {
        return await renderOnStage(stage, url, materialColor);
    } finally {
        disposePreviewStage(stage);
    }
}

export function disposeViewer(elementId) {
    const instance = viewers.get(elementId);
    if (!instance) return;
    viewers.delete(elementId);

    instance.disposed = true;
    if (instance.raf) cancelAnimationFrame(instance.raf);
    instance.resizeObserver?.disconnect();
    instance.controls?.dispose();

    if (instance.grid) {
        instance.scene.remove(instance.grid);
        instance.grid.geometry?.dispose();
        instance.grid.material?.dispose();
        instance.grid = null;
    }

    if (instance.shadowPlane) {
        instance.scene.remove(instance.shadowPlane);
        instance.shadowPlane.geometry?.dispose();
        instance.shadowPlane.material?.dispose();
        instance.shadowPlane = null;
    }

    if (instance.mesh) {
        instance.scene.remove(instance.mesh);
        instance.mesh.geometry?.dispose();
        if (Array.isArray(instance.mesh.material)) {
            instance.mesh.material.forEach((m) => m.dispose());
        } else {
            instance.mesh.material?.dispose();
        }
        instance.mesh = null;
    }

    instance.backdrop?.dispose();
    instance.backdrop = null;

    if (instance.renderer) {
        instance.renderer.dispose();
        instance.renderer.forceContextLoss?.();
        instance.renderer.domElement?.remove();
    }
}

export function disposeAll() {
    for (const id of [...viewers.keys()]) disposeViewer(id);
}

async function captureThumbnail(instance) {
    if (instance.disposed || !instance.renderer || !instance.modelId) return false;
    try {
        instance.renderer.render(instance.scene, instance.camera);
        const blob = await new Promise((resolve) => instance.renderer.domElement.toBlob(resolve, "image/png"));
        if (!blob) return false;

        const response = await fetch(`/api/models/${instance.modelId}/thumbnail`, {
            method: "POST",
            headers: { "Content-Type": "image/png" },
            body: blob
        });
        if (response.ok) {
            instance.dotNetRef?.invokeMethodAsync("OnThumbnailSaved");
            return true;
        }
        return false;
    } catch (err) {
        console.warn("PrintShelf: thumbnail capture failed", err);
        return false;
    }
}

function notifyError(dotNetRef, message) {
    console.error("PrintShelf viewer:", message);
    dotNetRef?.invokeMethodAsync("OnViewerError", message);
}
