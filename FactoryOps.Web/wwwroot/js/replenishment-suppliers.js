(() => {
    const form = document.getElementById("replenishmentForm");
    if (!form) return;
    const product = document.getElementById("ProductId"), supplier = document.getElementById("SupplierId");
    const manual = document.getElementById("manualSupplierField"), error = document.getElementById("supplierLoadError");
    const manage = document.getElementById("manageProductSuppliers"), edit = document.getElementById("editSelectedSupplier");
    const submit = document.getElementById("saveReplenishment");
    let pending;
    function sync() {
        manual.hidden = !!supplier.value;
        manage.hidden = !product.value;
        manage.href = `/Suppliers/Product/${encodeURIComponent(product.value)}`;
        edit.hidden = !supplier.value;
        edit.href = `/Suppliers/Edit/${encodeURIComponent(supplier.value)}?productId=${encodeURIComponent(product.value)}`;
    }
    supplier.addEventListener("change", sync);
    product.addEventListener("change", async () => {
        pending?.abort();
        const request = new AbortController(); pending = request;
        supplier.replaceChildren(new Option("Other / enter manually", ""));
        document.getElementById("Supplier").value = "";
        error.hidden = true; sync();
        if (!product.value) { supplier.disabled = false; submit.disabled = false; return; }
        supplier.disabled = true; submit.disabled = true;
        try {
            const url = new URL(form.dataset.suppliersUrl, location.origin);
            url.searchParams.set("productId", product.value);
            const response = await fetch(url, { signal: request.signal, cache: "no-store" });
            if (!response.ok) throw new Error();
            const data = await response.json();
            if (data.companyKey !== form.dataset.company) throw new Error();
            if (pending !== request) return;
            for (const item of data.suppliers) supplier.add(new Option(item.name + (item.isDefault ? " (Default)" : ""), item.id));
            supplier.value = String(data.suppliers.find(item => item.isDefault)?.id || "");
            sync();
        } catch (failure) {
            if (failure.name !== "AbortError" && pending === request) {
                error.textContent = "Supplier choices are unavailable. Reload and check the active company, or enter a supplier manually.";
                error.hidden = false;
            }
        } finally {
            if (pending === request) { supplier.disabled = false; submit.disabled = false; }
        }
    });
    sync();
})();
