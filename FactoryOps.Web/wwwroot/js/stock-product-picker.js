(() => {
    const select = document.getElementById("stockProductSelect"), button = document.getElementById("stockProductButton");
    if (!select || !button) return;
    const list = document.getElementById("stockProductOptions"), value = document.getElementById("stockProductValue");
    const options = Array.from(select.options);
    let active = 0;
    const items = options.map((option, index) => {
        const item = document.createElement("button");
        item.type = "button"; item.tabIndex = -1; item.setAttribute("role", "option");
        item.id = `stock-product-option-${option.value}`; item.dataset.value = option.value;
        item.className = "stock-product-option";
        item.addEventListener("click", () => choose(index));
        list.append(item); return item;
    });
    function highlight(index) {
        active = Math.max(0, Math.min(index, items.length - 1));
        items.forEach((item, i) => item.classList.toggle("stock-product-focused", i === active));
        if (items[active]) {
            button.setAttribute("aria-activedescendant", items[active].id);
            items[active].scrollIntoView({ block: "nearest" });
        }
    }
    function sync() {
        for (let i = 0; i < options.length; i++) {
            items[i].textContent = options[i].textContent;
            items[i].classList.toggle("stock-product-risk", options[i].dataset.risk === "true");
            items[i].setAttribute("aria-selected", String(options[i].selected));
        }
        const selected = select.selectedOptions[0];
        value.textContent = selected?.textContent || "No catalog products";
        button.classList.toggle("stock-product-risk", selected?.dataset.risk === "true");
        button.disabled = !options.length;
    }
    function close() {
        list.hidden = true; button.setAttribute("aria-expanded", "false"); button.removeAttribute("aria-activedescendant");
    }
    function open() {
        if (!items.length) return;
        list.hidden = false; button.setAttribute("aria-expanded", "true"); highlight(select.selectedIndex);
    }
    function choose(index) {
        if (!options[index]) return;
        select.value = options[index].value;
        sync(); close(); button.focus(); select.dispatchEvent(new Event("change", { bubbles: true }));
    }
    button.addEventListener("click", () => list.hidden ? open() : close());
    button.addEventListener("keydown", event => {
        if (["ArrowDown", "ArrowUp", "Home", "End", "Enter", " ", "Escape"].includes(event.key)) event.preventDefault();
        if (event.key === "Escape" || event.key === "Tab") close();
        else if (event.key === "Enter" || event.key === " ") list.hidden ? open() : choose(active);
        else if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
            if (list.hidden) open();
            else highlight(event.key === "Home" ? 0 : event.key === "End" ? items.length - 1 : active + (event.key === "ArrowDown" ? 1 : -1));
        }
    });
    document.addEventListener("click", event => { if (!button.parentElement.contains(event.target)) close(); });
    button.parentElement.addEventListener("focusout", event => { if (!button.parentElement.contains(event.relatedTarget)) close(); });
    select.addEventListener("change", sync); select.addEventListener("stockriskupdated", sync);
    document.getElementById("stockProductLabel").addEventListener("click", event => { event.preventDefault(); button.focus(); });
    sync(); select.hidden = true; button.hidden = false;
})();
