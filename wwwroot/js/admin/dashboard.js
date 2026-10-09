(() => {
    const cssVar = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    const recentOrdersEl = document.getElementById("recent-orders");

    document.getElementById("revenue-currency").textContent = AdminUI.currencyCode;

    document.getElementById("dashboard-date").textContent =
        new Date().toLocaleDateString("en-MY", { weekday: "long", day: "numeric", month: "long", year: "numeric" });

    Chart.defaults.plugins.legend.display = false;

    const setDelta = (id, today, yesterday, format) => {
        const el = document.getElementById(id);
        const diff = today - yesterday;
        if (diff === 0) {
            el.className = "text-muted";
            el.textContent = "Same as yesterday";
            return;
        }
        el.className = diff > 0 ? "text-success" : "text-danger";
        el.textContent = `${diff > 0 ? "+" : "-"}${format(Math.abs(diff))} vs yesterday`;
    };

    const showEmpty = (canvasId, isEmpty, message) => {
        const box = document.getElementById(canvasId).parentElement;
        box.querySelector(".chart-empty")?.remove();
        if (isEmpty) {
            box.insertAdjacentHTML("beforeend",
                `<p class="chart-empty text-muted small">${AdminUI.escapeHtml(message)}</p>`);
        }
    };

    const barChart = (canvasId, labels, values, label, horizontal = false) => new Chart(document.getElementById(canvasId), {
        type: "bar",
        data: { labels, datasets: [{ label, data: values, backgroundColor: cssVar("--bs-primary"), borderRadius: 4, maxBarThickness: 32 }] },
        options: {
            indexAxis: horizontal ? "y" : "x",
            scales: { [horizontal ? "x" : "y"]: { beginAtZero: true, ticks: { precision: 0 } } }
        }
    });

    const hourLabel = (hour) => new Date(2000, 0, 1, hour).toLocaleTimeString("en-MY", { hour: "numeric" });
    const dayLabel = (isoDate) => new Date(`${isoDate}T00:00:00`).toLocaleDateString("en-MY", { weekday: "short" });

    const render = (data) => {
        document.getElementById("kpi-revenue").textContent = AdminUI.formatCurrency(data.revenueToday);
        document.getElementById("kpi-orders").textContent = data.ordersToday;
        document.getElementById("kpi-reservations").textContent = data.reservationsToday;
        document.getElementById("kpi-aov").textContent = AdminUI.formatCurrency(data.averageOrderValueToday);

        setDelta("kpi-revenue-delta", data.revenueToday, data.revenueYesterday, AdminUI.formatCurrency);
        setDelta("kpi-orders-delta", data.ordersToday, data.ordersYesterday, String);
        setDelta("kpi-reservations-delta", data.reservationsToday, data.reservationsYesterday, String);
        setDelta("kpi-aov-delta", data.averageOrderValueToday, data.averageOrderValueYesterday, AdminUI.formatCurrency);

        new Chart(document.getElementById("chart-revenue"), {
            type: "line",
            data: {
                labels: data.revenueLast7Days.map(d => dayLabel(d.date)),
                datasets: [{ label: `Revenue (${AdminUI.currencyCode})`, data: data.revenueLast7Days.map(d => d.amount / 100), borderColor: cssVar("--bs-primary"), backgroundColor: cssVar("--bs-primary"), borderWidth: 2, tension: 0.3 }]
            },
            options: {
                interaction: { mode: "index", intersect: false },
                scales: { y: { beginAtZero: true } }
            }
        });
        barChart("chart-status", data.ordersByStatus.map(s => s.status), data.ordersByStatus.map(s => s.count), "Orders", true);
        barChart("chart-hourly", data.ordersByHour.map(h => hourLabel(h.hour)), data.ordersByHour.map(h => h.count), "Orders");
        barChart("chart-items", data.topItems.map(i => i.name), data.topItems.map(i => i.units), "Units sold", true);

        showEmpty("chart-revenue", data.revenueLast7Days.every(d => d.amount === 0), "No revenue in the last 7 days yet.");
        showEmpty("chart-status", data.ordersToday === 0, "No orders today yet.");
        showEmpty("chart-hourly", data.ordersToday === 0, "No orders today yet.");
        showEmpty("chart-items", data.topItems.length === 0, "No items sold in the last 7 days yet.");

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

    recentOrdersEl.innerHTML = AdminUI.tableMessageRow(5, "Loading...");

    AdminUI.get("Stats")
        .then(render)
        .catch(error => {
            recentOrdersEl.innerHTML = AdminUI.tableMessageRow(5, error.message, "text-danger");
            AdminUI.showError(error);
        });
})();
