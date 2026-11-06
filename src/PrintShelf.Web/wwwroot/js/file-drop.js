// Drag & drop bridge for Blazor's <InputFile> (which only offers a picker).
// A dropped FileList cannot be assigned through Blazor, so we copy it into the
// real input and dispatch a change event.

export function wireDropZone(zoneId, inputId) {
    const zone = document.getElementById(zoneId);
    const input = document.getElementById(inputId);
    if (!zone || !input || zone.dataset.dropWired === "1") return;
    zone.dataset.dropWired = "1";

    const activate = (on) => zone.classList.toggle("is-dragover", on);

    zone.addEventListener("dragenter", (e) => { e.preventDefault(); activate(true); });
    zone.addEventListener("dragover", (e) => { e.preventDefault(); activate(true); });
    zone.addEventListener("dragleave", (e) => {
        if (e.target === zone) activate(false);
    });
    zone.addEventListener("dragend", () => activate(false));

    zone.addEventListener("drop", (e) => {
        e.preventDefault();
        activate(false);
        const dropped = e.dataTransfer?.files;
        if (!dropped || dropped.length === 0) return;
        try {
            const transfer = new DataTransfer();
            for (const file of dropped) transfer.items.add(file);
            input.files = transfer.files;
            input.dispatchEvent(new Event("change", { bubbles: true }));
        } catch (err) {
            console.error("PrintShelf: drop failed", err);
        }
    });
}

export function clearInput(inputId) {
    const input = document.getElementById(inputId);
    if (input) input.value = "";
}
