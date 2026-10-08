// Mock data - replace with API calls once the backend is ready.
const dashboardData = {
    revenueToday: 482650,
    ordersToday: 64,
    reservationsToday: 18,
    revenueLast7Days: [
        { label: "Thu", amount: 3820.5 },
        { label: "Fri", amount: 5120.0 },
        { label: "Sat", amount: 6890.75 },
        { label: "Sun", amount: 6210.2 },
        { label: "Mon", amount: 3140.0 },
        { label: "Tue", amount: 4295.4 },
        { label: "Wed", amount: 4826.5 }
    ],
    ordersByStatus: { Pending: 9, Preparing: 14, Served: 41 },
    ordersByHour: [
        { hour: "11am", count: 4 }, { hour: "12pm", count: 11 }, { hour: "1pm", count: 13 },
        { hour: "2pm", count: 6 }, { hour: "3pm", count: 2 }, { hour: "4pm", count: 3 },
        { hour: "5pm", count: 5 }, { hour: "6pm", count: 9 }, { hour: "7pm", count: 7 }, { hour: "8pm", count: 4 }
    ],
    topItems: [
        { name: "Carbonara", units: 86 },
        { name: "Aglio Olio", units: 71 },
        { name: "Bolognese", units: 64 },
        { name: "Iced Lemon Tea", units: 58 },
        { name: "Pesto Penne", units: 42 }
    ],
    recentOrders: [
        { id: "ORD-1064", table: "T05", time: "19:42", total: 8650, status: "Pending" },
        { id: "ORD-1063", table: "T12", time: "19:35", total: 12400, status: "Preparing" },
        { id: "ORD-1062", table: "T03", time: "19:31", total: 5600, status: "Preparing" },
        { id: "ORD-1061", table: "T08", time: "19:20", total: 15890, status: "Served" },
        { id: "ORD-1060", table: "T01", time: "19:12", total: 4300, status: "Served" }
    ]
};

(() => {
    // Chart colour comes from --admin-chart-color in admin_pages.css.
    const chartColor = getComputedStyle(document.documentElement).getPropertyValue("--admin-chart-color").trim();

    document.getElementById("dashboard-date").textContent =
        new Date().toLocaleDateString("en-MY", { weekday: "long", day: "numeric", month: "long", year: "numeric" });

    document.getElementById("kpi-revenue").textContent = AdminUI.formatCurrency(dashboardData.revenueToday);
    document.getElementById("kpi-orders").textContent = dashboardData.ordersToday;
    document.getElementById("kpi-reservations").textContent = dashboardData.reservationsToday;
    document.getElementById("kpi-aov").textContent =
        AdminUI.formatCurrency(Math.round(dashboardData.revenueToday / dashboardData.ordersToday));

    // Single-series charts, so the legend is hidden; card headers name each chart.
    Chart.defaults.maintainAspectRatio = false;
    Chart.defaults.plugins.legend.display = false;

    const barChart = (canvasId, labels, values, label, horizontal = false) => new Chart(document.getElementById(canvasId), {
        type: "bar",
        data: { labels, datasets: [{ label, data: values, backgroundColor: chartColor, borderRadius: 4, maxBarThickness: 32 }] },
        options: {
            indexAxis: horizontal ? "y" : "x",
            scales: { [horizontal ? "x" : "y"]: { beginAtZero: true, ticks: { precision: 0 } } }
        }
    });

    new Chart(document.getElementById("chart-revenue"), {
        type: "line",
        data: {
            labels: dashboardData.revenueLast7Days.map(d => d.label),
            datasets: [{
                label: "Revenue (RM)",
                data: dashboardData.revenueLast7Days.map(d => d.amount),
                borderColor: chartColor,
                backgroundColor: chartColor,
                borderWidth: 2,
                tension: 0.3
            }]
        },
        options: {
            interaction: { mode: "index", intersect: false },
            scales: { y: { beginAtZero: true } }
        }
    });

    const statusLabels = Object.keys(dashboardData.ordersByStatus);
    barChart("chart-status", statusLabels, statusLabels.map(s => dashboardData.ordersByStatus[s]), "Orders", true);
    barChart("chart-hourly", dashboardData.ordersByHour.map(d => d.hour), dashboardData.ordersByHour.map(d => d.count), "Orders");
    barChart("chart-items", dashboardData.topItems.map(d => d.name), dashboardData.topItems.map(d => d.units), "Units sold", true);

    document.getElementById("recent-orders").innerHTML = dashboardData.recentOrders.map(o => `
        <tr>
            <td class="fw-medium">${AdminUI.escapeHtml(o.id)}</td>
            <td>${AdminUI.escapeHtml(o.table)}</td>
            <td>${AdminUI.escapeHtml(o.time)}</td>
            <td>${AdminUI.formatCurrency(o.total)}</td>
            <td>${AdminUI.statusBadge(o.status)}</td>
        </tr>`).join("");
})();
