(() => {
    const rangeForm = document.getElementById("range-form");
    const fromInput = document.getElementById("range-from");
    const toInput = document.getElementById("range-to");
    const byDayButton = document.getElementById("orders-by-day");
    const byHourButton = document.getElementById("orders-by-hour");
    const recentOrdersEl = document.getElementById("recent-orders");

    let revenueChart = null;
    let ordersChart = null;
    let itemsChart = null;
    let ordersByDay = { labels: [], counts: [] };
    let ordersByHour = { labels: [], counts: [] };
    let latestRequest = 0;

    document.getElementById("revenue-currency").textContent = AdminUI.currencyCode;

    const toIsoDate = (date) =>
        `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;

    const addDays = (isoDate, days) => {
        const date = new Date(`${isoDate}T00:00:00`);
        date.setDate(date.getDate() + days);
        return toIsoDate(date);
    };

    const dayLabel = (isoDate) => new Date(`${isoDate}T00:00:00`).toLocaleDateString("en-MY", { day: "numeric", month: "short" });
    const hourLabel = (hour) => new Date(2000, 0, 1, hour).toLocaleTimeString("en-MY", { hour: "numeric" });

    const setDelta = (id, current, previous, format) => {
        const el = document.getElementById(id);
        const diff = current - previous;
        if (diff === 0) {
            el.className = "text-muted";
            el.textContent = "Same as previous period";
            return;
        }
        el.className = diff > 0 ? "text-success" : "text-danger";
        el.textContent = `${diff > 0 ? "+" : "-"}${format(Math.abs(diff))} vs previous period`;
    };

    const showSummary = (data) => {
        document.getElementById("kpi-revenue").textContent = AdminUI.formatCurrency(data.revenue);
        document.getElementById("kpi-orders").textContent = data.orders;
        document.getElementById("kpi-reservations").textContent = data.reservations;
        document.getElementById("kpi-aov").textContent = AdminUI.formatCurrency(data.averageOrderValue);

        setDelta("kpi-revenue-delta", data.revenue, data.previousRevenue, AdminUI.formatCurrency);
        setDelta("kpi-orders-delta", data.orders, data.previousOrders, String);
        setDelta("kpi-reservations-delta", data.reservations, data.previousReservations, String);
        setDelta("kpi-aov-delta", data.averageOrderValue, data.previousAverageOrderValue, AdminUI.formatCurrency);
    };

    const showRevenueChart = (data) => {
        if (revenueChart) revenueChart.destroy();

        revenueChart = new ApexCharts(document.querySelector("#chart-revenue"), {
            chart: { type: "line", height: 300, toolbar: { show: false } },
            series: [{ name: "Revenue", data: data.revenueByDay.map(d => d.amount / 100) }],
            xaxis: { categories: data.revenueByDay.map(d => dayLabel(d.date)) },
            yaxis: { min: 0 },
            stroke: { curve: "smooth" },
            tooltip: { y: { formatter: (value) => AdminUI.formatCurrency(value * 100) } }
        });
        revenueChart.render();
    };

    const showOrdersChart = (data) => {
        ordersByDay = { labels: data.ordersByDay.map(d => dayLabel(d.date)), counts: data.ordersByDay.map(d => d.count) };
        ordersByHour = { labels: data.ordersByHour.map(h => hourLabel(h.hour)), counts: data.ordersByHour.map(h => h.count) };

        if (ordersChart) ordersChart.destroy();

        ordersChart = new ApexCharts(document.querySelector("#chart-orders"), {
            chart: { type: "bar", height: 300, toolbar: { show: false } },
            series: [{ name: "Orders", data: ordersByDay.counts }],
            xaxis: { categories: ordersByDay.labels },
            yaxis: { forceNiceScale: true, labels: { formatter: (value) => Math.round(value) } },
            dataLabels: { enabled: false }
        });
        ordersChart.render();

        byDayButton.classList.add("active");
        byHourButton.classList.remove("active");
    };

    const showTopItemsChart = (data) => {
        if (itemsChart) itemsChart.destroy();
        itemsChart = null;

        const itemsEl = document.querySelector("#chart-items");
        if (data.topItems.length === 0) {
            itemsEl.textContent = "No items sold in this date range.";
            return;
        }
        itemsEl.textContent = "";

        itemsChart = new ApexCharts(itemsEl, {
            chart: { type: "bar", height: 300, toolbar: { show: false } },
            plotOptions: { bar: { horizontal: true } },
            series: [{ name: "Units sold", data: data.topItems.map(i => i.units) }],
            xaxis: { categories: data.topItems.map(i => i.name), tickAmount: Math.min(data.topItems[0].units, 5), labels: { formatter: (value) => Math.round(value) } },
            tooltip: { x: { formatter: (value) => AdminUI.escapeHtml(value) } },
            dataLabels: { enabled: false }
        });
        itemsChart.render();
    };

    const showRecentOrders = (data) => {
        recentOrdersEl.innerHTML = data.recentOrders.length
            ? data.recentOrders.map(o => `
                <tr>
                    <td class="fw-medium">${AdminUI.escapeHtml(o.id)}</td>
                    <td>${AdminUI.escapeHtml(o.table ?? "-")}</td>
                    <td class="text-nowrap">${AdminUI.formatTime(o.createdAt)}</td>
                    <td>${AdminUI.formatCurrency(o.total)}</td>
                    <td>${AdminUI.statusBadge(o.status)}</td>
                </tr>`).join("")
            : AdminUI.tableMessageRow(5, "No orders yet.");
    };

    const loadDashboard = () => {
        if (toInput.value < fromInput.value) {
            AdminUI.showToast("The To date must be on or after the From date.", "danger");
            return;
        }

        const fromText = AdminUI.formatDate(`${fromInput.value}T00:00:00`);
        const toText = AdminUI.formatDate(`${toInput.value}T00:00:00`);
        document.getElementById("range-label").textContent = fromText === toText ? fromText : `${fromText} - ${toText}`;

        latestRequest++;
        const thisRequest = latestRequest;

        AdminUI.get("Stats", { from: fromInput.value, to: toInput.value })
            .then(data => {
                if (thisRequest !== latestRequest) return;
                showSummary(data);
                showRevenueChart(data);
                showOrdersChart(data);
                showTopItemsChart(data);
                showRecentOrders(data);
            })
            .catch(error => {
                if (thisRequest !== latestRequest) return;
                recentOrdersEl.innerHTML = AdminUI.tableMessageRow(5, error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    byDayButton.addEventListener("click", () => {
        if (!ordersChart) return;
        ordersChart.updateOptions({ series: [{ name: "Orders", data: ordersByDay.counts }], xaxis: { categories: ordersByDay.labels } });
        byDayButton.classList.add("active");
        byHourButton.classList.remove("active");
    });

    byHourButton.addEventListener("click", () => {
        if (!ordersChart) return;
        ordersChart.updateOptions({ series: [{ name: "Orders", data: ordersByHour.counts }], xaxis: { categories: ordersByHour.labels } });
        byHourButton.classList.add("active");
        byDayButton.classList.remove("active");
    });

    rangeForm.addEventListener("submit", (e) => {
        e.preventDefault();
        loadDashboard();
    });

    rangeForm.addEventListener("click", (e) => {
        const button = e.target.closest("[data-range]");
        if (!button) return;

        const today = rangeForm.dataset.today;
        if (button.dataset.range === "today") {
            fromInput.value = today;
        } else if (button.dataset.range === "week") {
            fromInput.value = addDays(today, -6);
        } else {
            fromInput.value = `${today.slice(0, 8)}01`;
        }
        toInput.value = today;
        loadDashboard();
    });

    recentOrdersEl.innerHTML = AdminUI.tableMessageRow(5, "Loading...");
    loadDashboard();
})();
