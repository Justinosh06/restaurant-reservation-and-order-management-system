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
    const css = getComputedStyle(document.documentElement);
    const token = (name) => css.getPropertyValue(name).trim();
    const series1 = token("--series-1");
    const gridColor = token("--grid-line");

    document.getElementById("dashboard-date").textContent =
        new Date().toLocaleDateString("en-MY", { weekday: "long", day: "numeric", month: "long", year: "numeric" });

    document.getElementById("kpi-revenue").textContent = AdminUI.formatCurrency(dashboardData.revenueToday);
    document.getElementById("kpi-orders").textContent = dashboardData.ordersToday;
    document.getElementById("kpi-reservations").textContent = dashboardData.reservationsToday;
    document.getElementById("kpi-aov").textContent =
        AdminUI.formatCurrency(Math.round(dashboardData.revenueToday / dashboardData.ordersToday));

    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
    Chart.defaults.color = token("--admin-text-secondary");
    Chart.defaults.maintainAspectRatio = false;
    Chart.defaults.plugins.legend.display = false;
    Chart.defaults.plugins.tooltip.backgroundColor = "#0b0b0b";
    Chart.defaults.plugins.tooltip.padding = 10;
    Chart.defaults.plugins.tooltip.cornerRadius = 8;
    Chart.defaults.plugins.tooltip.displayColors = false;

    const valueAxis = (ticks = {}) => ({
        beginAtZero: true,
        grid: { color: gridColor, drawTicks: false },
        border: { display: false },
        ticks: { padding: 8, ...ticks }
    });
    const categoryAxis = { grid: { display: false }, border: { color: gridColor } };

    const verticalBar = {
        backgroundColor: series1,
        hoverBackgroundColor: token("--status-preparing"),
        borderRadius: { topLeft: 4, topRight: 4 },
        borderSkipped: "start",
        maxBarThickness: 28
    };
    const horizontalBar = { ...verticalBar, borderRadius: { topRight: 4, bottomRight: 4 } };

    new Chart(document.getElementById("chart-revenue"), {
        type: "line",
        data: {
            labels: dashboardData.revenueLast7Days.map(d => d.label),
            datasets: [{
                label: "Revenue",
                data: dashboardData.revenueLast7Days.map(d => d.amount),
                borderColor: series1,
                backgroundColor: series1 + "1a",
                fill: true,
                borderWidth: 2,
                tension: 0.3,
                pointRadius: 4,
                pointHoverRadius: 6,
                pointBackgroundColor: series1,
                pointBorderColor: "#ffffff",
                pointBorderWidth: 2
            }]
        },
        options: {
            interaction: { mode: "index", intersect: false },
            scales: { x: categoryAxis, y: valueAxis({ callback: v => "RM " + v.toLocaleString() }) },
            plugins: {
                tooltip: { callbacks: { label: ctx => "RM " + ctx.parsed.y.toLocaleString("en-MY", { minimumFractionDigits: 2 }) } }
            }
        }
    });

    const statusLabels = Object.keys(dashboardData.ordersByStatus);
    new Chart(document.getElementById("chart-status"), {
        type: "bar",
        data: {
            labels: statusLabels,
            datasets: [{ label: "Orders", data: statusLabels.map(s => dashboardData.ordersByStatus[s]), ...horizontalBar, maxBarThickness: 32 }]
        },
        options: {
            indexAxis: "y",
            scales: { x: valueAxis({ precision: 0 }), y: categoryAxis },
            plugins: { tooltip: { callbacks: { label: ctx => ctx.parsed.x + " orders" } } }
        }
    });

    new Chart(document.getElementById("chart-hourly"), {
        type: "bar",
        data: {
            labels: dashboardData.ordersByHour.map(d => d.hour),
            datasets: [{ label: "Orders", data: dashboardData.ordersByHour.map(d => d.count), ...verticalBar }]
        },
        options: {
            scales: { x: categoryAxis, y: valueAxis({ precision: 0 }) },
            plugins: { tooltip: { callbacks: { label: ctx => ctx.parsed.y + " orders" } } }
        }
    });

    new Chart(document.getElementById("chart-items"), {
        type: "bar",
        data: {
            labels: dashboardData.topItems.map(d => d.name),
            datasets: [{ label: "Units sold", data: dashboardData.topItems.map(d => d.units), ...horizontalBar, maxBarThickness: 24 }]
        },
        options: {
            indexAxis: "y",
            scales: { x: valueAxis({ precision: 0 }), y: categoryAxis },
            plugins: { tooltip: { callbacks: { label: ctx => ctx.parsed.x + " units" } } }
        }
    });

    document.getElementById("recent-orders").innerHTML = dashboardData.recentOrders.map(o => `
        <tr>
            <td class="fw-medium">${AdminUI.escapeHtml(o.id)}</td>
            <td>${AdminUI.escapeHtml(o.table)}</td>
            <td>${AdminUI.escapeHtml(o.time)}</td>
            <td>${AdminUI.formatCurrency(o.total)}</td>
            <td><span class="status-pill ${o.status.toLowerCase()}">${AdminUI.escapeHtml(o.status)}</span></td>
        </tr>`).join("");
})();
