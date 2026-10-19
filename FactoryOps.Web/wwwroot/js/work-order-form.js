(() => {
    "use strict";
    const form = document.getElementById("orderForm");
    if (!form) return;
    const search = document.getElementById("customerSearch");
    const results = document.getElementById("searchResults");
    const customerId = document.getElementById("CustomerId");
    const selected = document.getElementById("selectedCustomer");
    const deliveryType = document.getElementById("DeliveryType");
    const sites = document.getElementById("ConstructionSiteId");
    const siteMessage = document.getElementById("siteMessage");
    const message = document.getElementById("orderMessage");
    const items = document.getElementById("orderItemsContainer");
    const save = document.getElementById("saveOrderButton");
    const productFilter = document.getElementById("productFilter");
    const clearProductFilterButton = document.getElementById("clearProductFilterButton");
    const productFilterMessage = document.getElementById("productFilterMessage");
    function filterProducts() {
        // Keep numeric codes as text so leading zeroes remain significant.
        const query = productFilter.value.trim().toLowerCase();
        const matches = new Set();
        items.querySelectorAll("select[data-field='ProductId'] option").forEach(option => {
            const matched = (option.dataset.search || "").includes(query);
            if (option.value && matched) matches.add(option.value);
            option.hidden = Boolean(option.value) && !option.selected && !matched;
        });
        clearProductFilterButton.classList.toggle("d-none", !query);
        productFilterMessage.classList.toggle("d-none", !query);
        productFilterMessage.textContent = query
            ? `${matches.size} matching product(s). Current selections are kept. Selecting a product restores the full list.`
            : "";
    }
    function clearProductFilter() {
        productFilter.value = "";
        filterProducts();
    }
    productFilter.addEventListener("input", filterProducts);
    productFilter.addEventListener("search", filterProducts);
    clearProductFilterButton.addEventListener("click", () => {
        clearProductFilter();
        productFilter.focus();
    });
    items.addEventListener("change", event => {
        if (event.target.matches("select[data-field='ProductId']") && event.target.value)
            clearProductFilter();
    });
    let timer;
    let searchRequest;
    let siteRequest;
    let submitting = false;

    function updateDelivery() {
        const isSite = deliveryType.value === "Site";
        document.getElementById("siteSection").classList.toggle("d-none", !isSite);
        document.getElementById("companyMessage").classList.toggle("d-none", isSite);
        sites.disabled = !isSite;
        sites.required = isSite;
    }
    deliveryType.addEventListener("change", updateDelivery);
    updateDelivery();

    async function loadSites(id) {
        if (siteRequest) siteRequest.abort();
        const request = new AbortController();
        siteRequest = request;
        sites.replaceChildren(new Option("Select an active construction site...", ""));
        siteMessage.textContent = "Loading sites...";
        try {
            const url = new URL(form.dataset.sitesUrl, window.location.origin);
            url.searchParams.set("customerId", id);
            const response = await fetch(url, { signal: request.signal });
            if (!response.ok) throw new Error("Site lookup failed");
            const data = await response.json();
            if (customerId.value !== String(id)) return;
            const active = data.filter(site => site.status === "Active");
            active.forEach(site => sites.add(new Option(
                `${site.siteName} - ${site.address}, ${site.suburb}`, String(site.id))));
            siteMessage.textContent = active.length ? `${active.length} active site(s) available.`
                : "No active sites. Add one in Customer Management or choose Company Address.";
        } catch (error) {
            if (error.name !== "AbortError" && customerId.value === String(id))
                siteMessage.textContent = "Unable to load construction sites. Select the customer again to retry.";
        }
    }

    search.addEventListener("input", () => {
        clearTimeout(timer);
        if (searchRequest) searchRequest.abort();
        if (siteRequest) siteRequest.abort();
        customerId.value = "0";
        selected.classList.add("d-none");
        sites.replaceChildren(new Option("Select an active construction site...", ""));
        siteMessage.textContent = "Select a customer to load delivery locations.";
        results.replaceChildren();
        const query = search.value.trim();
        if (query.length < 2) return;
        timer = setTimeout(async () => {
            const request = new AbortController();
            searchRequest = request;
            try {
                const url = new URL(form.dataset.searchUrl, window.location.origin);
                url.searchParams.set("q", query);
                const response = await fetch(url, { signal: request.signal });
                if (!response.ok) throw new Error("Search failed");
                const customers = await response.json();
                if (search.value.trim() !== query) return;
                results.replaceChildren();
                if (!customers.length) results.textContent = "No matching customers. Use + New Customer to add one.";
                customers.forEach(customer => {
                    const button = document.createElement("button");
                    button.type = "button";
                    button.className = "list-group-item list-group-item-action";
                    button.textContent = `${customer.companyName} - ${customer.contactName || ""} ${customer.phone || ""}`;
                    button.addEventListener("click", () => {
                        customerId.value = String(customer.id);
                        search.value = customer.companyName;
                        document.getElementById("selectedCustomerName").textContent = customer.companyName;
                        selected.classList.remove("d-none");
                        results.replaceChildren();
                        loadSites(customer.id);
                    });
                    results.appendChild(button);
                });
            } catch (error) {
                if (error.name !== "AbortError") results.textContent = "Unable to search customers. Please try again.";
            }
        }, 300);
    });

    function reindexItems() {
        items.querySelectorAll(".order-item").forEach((row, index) => {
            row.querySelectorAll("[data-field]").forEach(input => {
                const oldId = input.id;
                const field = input.dataset.field;
                input.name = `Items[${index}].${field}`;
                input.id = `Items_${index}__${field}`;
                row.querySelectorAll("label").forEach(label => {
                    if (label.htmlFor === oldId || label.dataset.label === field) label.htmlFor = input.id;
                });
            });
            row.querySelectorAll("[data-valmsg-for]").forEach(span => {
                span.dataset.valmsgFor = span.dataset.valmsgFor.replace(/Items\[\d+\]/, `Items[${index}]`);
            });
        });
    }
    document.getElementById("addItemButton").addEventListener("click", () => {
        items.appendChild(document.getElementById("orderItemTemplate").content.cloneNode(true));
        reindexItems();
        clearProductFilter();
        items.lastElementChild.querySelector("select").focus();
    });
    items.addEventListener("click", event => {
        const button = event.target.closest(".remove-item-btn");
        if (!button) return;
        button.closest(".order-item").remove();
        reindexItems();
        filterProducts();
    });
    const stockWarning = document.getElementById("stockWarningDialog");
    if (stockWarning && typeof stockWarning.showModal === "function") {
        function showStockWarning() {
            if (stockWarning.open) stockWarning.close();
            stockWarning.showModal();
        }
        document.getElementById("reviewStockWarning").addEventListener("click", showStockWarning);
        document.getElementById("declineStockWarning").addEventListener("click", () => stockWarning.close());
        showStockWarning();
    }
    form.addEventListener("submit", event => {
        message.textContent = "";
        if (submitting) { event.preventDefault(); return; }
        if (Number(customerId.value) <= 0) message.textContent = "Select a customer from the search results.";
        else if (!items.querySelector(".order-item")) message.textContent = "Add at least one valid order item.";
        else if (deliveryType.value === "Site" && !sites.value) message.textContent = "Select an active construction site.";
        if (message.textContent) { event.preventDefault(); return; }
        submitting = true;
        save.disabled = true;
        save.textContent = "Saving...";
    });
    window.addEventListener("pageshow", () => {
        filterProducts();
        submitting = false;
        save.disabled = false;
        save.textContent = form.action.includes("/Edit") ? "Save Changes" : "Create Order";
    });
})();
