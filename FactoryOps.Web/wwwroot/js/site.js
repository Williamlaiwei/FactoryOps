// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    "use strict";
    const refreshFilePickers = [];
    document.querySelectorAll('input[type="file"]').forEach(input => {
        const picker = document.createElement("div");
        picker.className = "english-file-picker";
        // Keep the existing spacing on the visible control rather than its transparent input.
        [...input.classList].filter(name => /^m[trblxy]?-/.test(name)).forEach(name => {
            picker.classList.add(name);
            input.classList.remove(name);
        });
        const button = document.createElement("span");
        button.className = "file-picker-button";
        button.textContent = "Choose File";
        button.setAttribute("aria-hidden", "true");
        const fileName = document.createElement("span");
        fileName.className = "file-picker-name";
        fileName.id = input.id + "-file-name";
        fileName.setAttribute("aria-live", "polite");
        input.before(picker);
        picker.append(button, fileName, input);
        // The real input still handles labels, keyboard focus, file selection and form submission.
        input.classList.add("english-file-input");
        input.title = "Choose a file";
        input.setAttribute("aria-describedby", [input.getAttribute("aria-describedby"), fileName.id].filter(Boolean).join(" "));
        function refresh() {
            const files = [...input.files];
            fileName.textContent = files.length > 1 ? `${files.length} files selected` : files[0]?.name || "No file chosen";
            input.setCustomValidity(input.required && files.length === 0 ? "Choose a file before uploading." : "");
        }
        input.addEventListener("change", refresh);
        input.form?.addEventListener("reset", () => queueMicrotask(refresh));
        refreshFilePickers.push(refresh);
        refresh();
    });
    window.addEventListener("pageshow", () => refreshFilePickers.forEach(refresh => refresh()));
})();
