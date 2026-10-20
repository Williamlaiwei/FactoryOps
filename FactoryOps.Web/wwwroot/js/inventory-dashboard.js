(() => {
    "use strict";
    const dashboard = document.getElementById("stockDashboard");
    if (!dashboard) return;
    const product = document.getElementById("stockProductSelect"), range = document.getElementById("projectionMonths");
    const svg = document.getElementById("stockProjectionChart"), ns = "http://www.w3.org/2000/svg";
    const error = document.getElementById("projectionError"), notice = document.getElementById("projectionNotice");
    const tooltip = document.getElementById("projectionTooltip"), rows = document.getElementById("projectionRows");
    let pending, days = [], selected = 0, cursor, currentData;
    const box = { left: 78, right: 975, top: 22, bottom: 290 };
    const colors = { actual: "#1765d1", withIncoming: "#16803c", available: "#d32f2f" };
    function element(tag, attrs = {}, text) {
        const node = document.createElementNS(ns, tag);
        for (const [name, value] of Object.entries(attrs)) node.setAttribute(name, value);
        if (text !== undefined) node.textContent = text;
        svg.append(node); return node;
    }
    function inspect(index) {
        if (!days.length) return;
        selected = Math.max(0, Math.min(days.length - 1, index));
        const day = days[selected], unit = currentData.unit;
        cursor.setAttribute("x1", x(selected)); cursor.setAttribute("x2", x(selected));
        tooltip.textContent = `${day.date} | Actual: ${day.actual} ${unit} | With incoming: ${day.withIncoming} | After outbound holds: ${day.available} | Incoming on date: +${day.incoming} | Outbound on date: -${day.outgoing}`;
    }
    function x(index) { return box.left + index / Math.max(1, days.length - 1) * (box.right - box.left); }
    function draw(data) {
        const option = Array.from(product.options).find(item => item.value === String(data?.productId));
        if (option && data.stockAlert) {
            option.dataset.risk = String(data.stockRisk); option.dataset.status = data.stockAlert;
            option.textContent = option.dataset.label + (data.stockAlert !== "Sufficient" ? " — " + data.stockAlert : "");
            product.dispatchEvent(new Event("stockriskupdated"));
        }
        currentData = data; days = data?.days || []; rows.replaceChildren();
        const small = svg.clientWidth < 600, width = small ? 480 : 1000, font = small ? 16 : 12;
        svg.setAttribute("viewBox", `0 0 ${width} 340`); box.right = width - 25;
        svg.querySelectorAll("line,polyline,text,rect").forEach(node => node.remove());
        const unknown = data?.onHand === null || !data;
        document.getElementById("projectionOnHand").textContent = unknown ? "Unknown" : `${data.onHand} ${data.unit}`;
        document.getElementById("projectionIncoming").textContent = data ? `${data.incomingHeld} ${data.unit}` : "-";
        document.getElementById("projectionOutgoing").textContent = data ? `${data.outboundHeld} ${data.unit}` : "-";
        document.getElementById("projectionTitle").textContent = data ? `${data.sku} - ${data.name} | ${data.startDate} to ${data.endDate}` : "No catalog products";
        document.getElementById("projectionStockLink").href = data ? `/Inventory/Details/${data.productId}` : "/Inventory";
        const messages = [];
        if (unknown) messages.push("Start tracking this product and enter a physical stock count to view its forecast.");
        if (data?.overdueReceipts) messages.push(`${data.overdueReceipts} overdue incoming hold(s) are excluded until received or rescheduled.`);
        if (data?.needsReview) messages.push("Some outbound holds need stock/unit review. Their uncertain quantities are excluded from the chart.");
        notice.textContent = messages.join(" "); notice.classList.toggle("d-none", !messages.length);
        if (!days.length) { tooltip.textContent = "No measured stock projection available."; element("text", { x: width / 2, y: 155, "text-anchor": "middle", fill: "#6c757d" }, "Physical stock count required"); return; }
        const values = days.flatMap(day => [day.actual, day.withIncoming, day.available]);
        let min = Math.min(0, ...values), max = Math.max(1, ...values);
        const padding = Math.max(1, (max - min) * .08); min -= padding; max += padding;
        const y = value => box.bottom - (value - min) / (max - min) * (box.bottom - box.top);
        for (let tick = 0; tick <= 5; tick++) {
            const value = min + (max - min) * tick / 5, top = y(value);
            element("line", { x1: box.left, x2: box.right, y1: top, y2: top, stroke: "#e3e7eb" });
            element("text", { x: box.left - 10, y: top + 4, "text-anchor": "end", "font-size": font, fill: "#495057" }, Math.round(value).toLocaleString("en-AU"));
        }
        element("line", { x1: box.left, x2: box.right, y1: y(0), y2: y(0), stroke: "#85909c", "stroke-dasharray": "4 4" });
        // Step lines preserve the discrete date of receipts and outbound reservations.
        for (const key of ["withIncoming", "available", "actual"]) {
            const points = days.flatMap((day, index) => index === 0 ? [`${x(0)},${y(day[key])}`] :
                [`${x(index)},${y(days[index - 1][key])}`, `${x(index)},${y(day[key])}`]).join(" ");
            element("polyline", { points, fill: "none", stroke: colors[key], "stroke-width": key === "actual" ? 2 : 3,
                "stroke-dasharray": key === "actual" ? "6 4" : "none", "data-series": key });
        }
        const dateTicks = small ? 2 : 4;
        for (let tick = 0; tick <= dateTicks; tick++) {
            const index = Math.round((days.length - 1) * tick / dateTicks);
            element("text", { x: x(index), y: 318, "text-anchor": "middle", "font-size": font, fill: "#495057" }, days[index].date.slice(5));
        }
        cursor = element("line", { x1: box.left, x2: box.left, y1: box.top, y2: box.bottom, stroke: "#343a40", "stroke-dasharray": "2 3" });
        for (const day of days) {
            const tr = document.createElement("tr");
            for (const value of [day.date, day.actual, day.withIncoming, day.available, day.incoming, day.outgoing]) {
                const td = document.createElement("td"); td.textContent = String(value); tr.append(td);
            }
            if (day.available < 0) tr.classList.add("table-danger"); rows.append(tr);
        }
        inspect(0);
    }
    async function refresh() {
        if (!product.value) return;
        pending?.abort(); const request = new AbortController(); pending = request;
        error.classList.add("d-none"); dashboard.setAttribute("aria-busy", "true");
        try {
            const url = new URL(dashboard.dataset.url, location.origin);
            url.searchParams.set("productId", product.value); url.searchParams.set("months", range.value);
            const response = await fetch(url, { signal: request.signal, cache: "no-store" });
            if (!response.ok) throw new Error("Forecast unavailable. Reload the page and check the active company.");
            const data = await response.json();
            if (data.companyKey !== dashboard.dataset.company) throw new Error("The active company changed. Reload the dashboard.");
            if (request !== pending) return;
            draw(data); history.replaceState(null, "", `/Dashboard?productId=${data.productId}&months=${data.months}`);
        } catch (failure) {
            if (failure.name !== "AbortError" && request === pending) {
                error.textContent = failure.message; error.classList.remove("d-none");
                currentData = null; days = []; rows.replaceChildren();
                svg.querySelectorAll("line,polyline,text,rect").forEach(node => node.remove());
                for (const id of ["projectionOnHand", "projectionIncoming", "projectionOutgoing"]) document.getElementById(id).textContent = "Unavailable";
                document.getElementById("projectionTitle").textContent = "Forecast unavailable";
                document.getElementById("projectionStockLink").href = "/Inventory";
                tooltip.textContent = "Reload or choose a product to retry."; notice.classList.add("d-none");
            }
        } finally { if (request === pending) dashboard.setAttribute("aria-busy", "false"); }
    }
    svg.addEventListener("pointermove", event => {
        const bounds = svg.getBoundingClientRect(), position = (event.clientX - bounds.left) / bounds.width * svg.viewBox.baseVal.width;
        inspect(Math.round((position - box.left) / (box.right - box.left) * (days.length - 1)));
    });
    svg.addEventListener("keydown", event => {
        if (event.key === "ArrowLeft" || event.key === "ArrowRight") { event.preventDefault(); inspect(selected + (event.key === "ArrowLeft" ? -1 : 1)); }
    });
    document.getElementById("projectionControls").addEventListener("submit", event => { event.preventDefault(); refresh(); });
    product.addEventListener("change", refresh); range.addEventListener("change", refresh);
    draw(JSON.parse(document.getElementById("initialProjection").textContent));
    let lastWidth = svg.clientWidth;
    new ResizeObserver(() => { if (Math.abs(svg.clientWidth - lastWidth) > 1) { lastWidth = svg.clientWidth; draw(currentData); } }).observe(document.getElementById("stockChartHost"));
})();
