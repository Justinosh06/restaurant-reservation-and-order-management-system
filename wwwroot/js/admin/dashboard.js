(() => {
    const rangeForm = document.getElementById("range-form");
    const fromInput = document.getElementById("range-from");
    const toInput = document.getElementById("range-to");
    const byDayButton = document.getElementById("orders-by-day");
    const byHourButton = document.getElementById("orders-by-hour");
    const bookingsByDateButton = document.getElementById("bookings-by-date");
    const bookingsBySlotButton = document.getElementById("bookings-by-slot");
    const recentOrdersEl = document.getElementById("recent-orders");
    const exportButton = document.getElementById("export-button");

    // Charts use the page's text colours so they also work in dark mode.
    const pageStyle = getComputedStyle(document.body);
    const INK = pageStyle.getPropertyValue("--bs-emphasis-color").trim() || "#212529";
    const MUTED = pageStyle.getPropertyValue("--bs-secondary-color").trim() || "#6c757d";

    let revenueChart = null;
    let ordersChart = null;
    let itemsChart = null;
    const TABLES_SHOWN = 6;
    let utilizationTables = [];
    let showAllTables = false;
    let reservationsChart = null;
    let reservationsByDate = { labels: [], bookings: [], guests: [] };
    let reservationsBySlot = { labels: [], bookings: [], guests: [] };
    let sparklines = [];
    let ordersByDay = { labels: [], counts: [] };
    let ordersByHour = { labels: [], counts: [] };
    let latestRequest = 0;
    let selectedRange = null; // "today", "week", "month" or null for dates picked in the calendar

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

    const baseChart = (type) => ({
        type,
        height: 300,
        toolbar: { show: false },
        fontFamily: "inherit",
        foreColor: MUTED
    });

    // { word: "previous" or "last month", dates: "1 Sept 2026 - 30 Sept 2026" }
    const comparisonFor = (data) => ({
        word: data.compareWholeMonth ? "last month" : "previous",
        dates: `${AdminUI.formatDate(`${data.previousFrom}T00:00:00`)} - ${AdminUI.formatDate(`${data.previousTo}T00:00:00`)}`
    });

    // lowerIsBetter: a fall shows in green (e.g. cancellation rate). neutral: no green/red (e.g. booking lead time).
    const setDelta = (id, current, previous, format, comparison, { lowerIsBetter = false, neutral = false } = {}) => {
        const el = document.getElementById(id);
        const diff = Math.round((current - previous) * 10) / 10;
        el.title = `Compared with ${comparison.dates}`;
        if (diff === 0) {
            el.className = "text-body-secondary";
            el.textContent = `Same as ${comparison.word}`;
            return;
        }
        el.className = neutral
            ? "text-body-secondary fw-semibold"
            : (diff > 0) !== lowerIsBetter ? "text-success fw-semibold" : "text-danger fw-semibold";
        el.innerHTML = `<i class="fa-solid ${diff > 0 ? "fa-arrow-trend-up" : "fa-arrow-trend-down"} me-1"></i>`
            + `${diff > 0 ? "+" : "-"}${AdminUI.escapeHtml(format(Math.abs(diff)))} <span class="fw-normal text-body-secondary">vs ${comparison.word}</span>`;
    };

    // Small bar charts inside the KPI cards, one bar per day. A single day has no trend, so nothing is drawn.
    const showSparkline = (selector, values) => {
        if (values.length < 2) return;
        const chart = new ApexCharts(document.querySelector(selector), {
            chart: { type: "bar", width: 90, height: 40, sparkline: { enabled: true } },
            series: [{ data: values }],
            colors: [INK],
            plotOptions: { bar: { columnWidth: "50%" } },
            tooltip: { enabled: false }
        });
        chart.render();
        sparklines.push(chart);
    };

    const showSummary = (data) => {
        document.getElementById("kpi-revenue").textContent = AdminUI.formatCurrency(data.revenue);
        document.getElementById("kpi-orders").textContent = data.orders;
        document.getElementById("kpi-reservations").textContent = data.reservations;
        document.getElementById("kpi-aov").textContent = AdminUI.formatCurrency(data.averageOrderValue);
        document.getElementById("revenue-total").textContent = AdminUI.formatCurrency(data.revenue);

        const comparison = comparisonFor(data);
        setDelta("kpi-revenue-delta", data.revenue, data.previousRevenue, AdminUI.formatCurrency, comparison);
        setDelta("kpi-orders-delta", data.orders, data.previousOrders, String, comparison);
        setDelta("kpi-reservations-delta", data.reservations, data.previousReservations, String, comparison);
        setDelta("kpi-aov-delta", data.averageOrderValue, data.previousAverageOrderValue, AdminUI.formatCurrency, comparison);

        sparklines.forEach(chart => chart.destroy());
        sparklines = [];
        showSparkline("#spark-revenue", data.revenueByDay.map(d => d.amount));
        showSparkline("#spark-orders", data.ordersByDay.map(d => d.count));
        showSparkline("#spark-reservations", data.reservationsByDay.map(d => d.count));
        showSparkline("#spark-aov", data.revenueByDay.map(d => d.orders ? Math.round(d.amount / d.orders) : 0));
    };

    // A single day is shown hour by hour, because one point on a line chart tells nothing.
    const showRevenueChart = (data) => {
        if (revenueChart) revenueChart.destroy();

        const oneDay = data.revenueByDay.length === 1;
        const amounts = oneDay ? data.revenueByHour.map(h => h.amount) : data.revenueByDay.map(d => d.amount);
        const labels = oneDay ? data.revenueByHour.map(h => hourLabel(h.hour)) : data.revenueByDay.map(d => dayLabel(d.date));

        revenueChart = new ApexCharts(document.querySelector("#chart-revenue"), {
            chart: baseChart("area"),
            series: [{ name: "Revenue", data: amounts.map(amount => amount / 100) }],
            colors: [INK],
            xaxis: { categories: labels },
            yaxis: { min: 0, labels: { formatter: (value) => Math.round(value).toLocaleString("en-MY") } },
            stroke: { curve: "smooth", width: 2 },
            fill: { type: "gradient", gradient: { opacityFrom: 0.25, opacityTo: 0 } },
            dataLabels: { enabled: false },
            grid: { borderColor: "rgba(127, 127, 127, 0.2)", strokeDashArray: 4 },
            tooltip: { y: { formatter: (value) => AdminUI.formatCurrency(value * 100) } }
        });
        revenueChart.render();
    };

    const showOrdersView = (view) => {
        const counts = view === "hour" ? ordersByHour : ordersByDay;
        ordersChart.updateOptions({ series: [{ name: "Orders", data: counts.counts }], xaxis: { categories: counts.labels } });

        for (const [button, active] of [[byDayButton, view === "day"], [byHourButton, view === "hour"]]) {
            button.classList.toggle("active", active);
            button.classList.toggle("btn-dark", active);
        }
    };

    const showOrdersChart = (data) => {
        ordersByDay = { labels: data.ordersByDay.map(d => dayLabel(d.date)), counts: data.ordersByDay.map(d => d.count) };
        ordersByHour = { labels: data.ordersByHour.map(h => hourLabel(h.hour)), counts: data.ordersByHour.map(h => h.count) };

        if (ordersChart) ordersChart.destroy();

        ordersChart = new ApexCharts(document.querySelector("#chart-orders"), {
            chart: baseChart("bar"),
            series: [{ name: "Orders", data: ordersByDay.counts }],
            colors: [INK],
            xaxis: { categories: ordersByDay.labels },
            yaxis: { forceNiceScale: true, labels: { formatter: (value) => Math.round(value) } },
            plotOptions: { bar: { borderRadius: 3, columnWidth: "55%" } },
            grid: { borderColor: "rgba(127, 127, 127, 0.2)", strokeDashArray: 4 },
            dataLabels: { enabled: false }
        });
        ordersChart.render();
        showOrdersView(data.ordersByDay.length === 1 ? "hour" : "day");
    };

    const showTopItemsChart = (data) => {
        if (itemsChart) itemsChart.destroy();
        itemsChart = null;

        const itemsEl = document.querySelector("#chart-items");
        if (data.topItems.length === 0) {
            itemsEl.innerHTML = `<p class="text-body-secondary text-center py-5 mb-0">No items sold in this date range.</p>`;
            return;
        }
        itemsEl.textContent = "";

        itemsChart = new ApexCharts(itemsEl, {
            chart: baseChart("bar"),
            plotOptions: { bar: { horizontal: true, borderRadius: 3, barHeight: "55%" } },
            series: [{ name: "Units sold", data: data.topItems.map(i => i.units) }],
            colors: [INK],
            xaxis: { categories: data.topItems.map(i => i.name), tickAmount: Math.min(data.topItems[0].units, 5), labels: { formatter: (value) => Math.round(value) } },
            grid: { borderColor: "rgba(127, 127, 127, 0.2)", strokeDashArray: 4 },
            tooltip: { x: { formatter: (value) => AdminUI.escapeHtml(value) } },
            dataLabels: { enabled: false }
        });
        itemsChart.render();
    };

    // 0.4567 -> 45.7 (a percentage with one decimal)
    const toPercent = (rate) => Math.round(rate * 1000) / 10;

    const plural = (count, word) => `${count} ${count === 1 ? word : `${word}s`}`;

    const showUtilization = (data) => {
        const utilization = data.utilization;
        const valueEl = document.getElementById("kpi-utilization");
        const detailEl = document.getElementById("utilization-detail");
        const deltaEl = document.getElementById("kpi-utilization-delta");

        if (utilization.tableCount === 0 || utilization.rate === null) {
            valueEl.textContent = "-";
            deltaEl.innerHTML = "&nbsp;";
            detailEl.textContent = utilization.tableCount === 0
                ? "No tables have been set up yet."
                : "The restaurant wasn't open in this date range.";
            utilizationTables = [];
            showUtilizationTables();
            return;
        }

        valueEl.textContent = `${toPercent(utilization.rate)}%`;
        detailEl.textContent = `${utilization.usedHours} of ${utilization.openTableHours} table-hours in use · ${plural(utilization.visits, "visit")}`
            + (utilization.visits ? ` · avg. ${AdminUI.formatDuration(utilization.averageVisitMinutes)}` : "");

        if (utilization.previousRate === null) {
            deltaEl.className = "text-body-secondary";
            deltaEl.textContent = "No earlier data";
        } else {
            setDelta("kpi-utilization-delta", toPercent(utilization.rate), toPercent(utilization.previousRate),
                (points) => `${points.toFixed(1)} pts`, comparisonFor(data));
        }

        utilizationTables = [...utilization.tables].sort((a, b) =>
            b.rate - a.rate || a.id.localeCompare(b.id, undefined, { numeric: true }));
        showUtilizationTables();
    };

    // Busiest tables first, each with its rate written out. Only the first few until "Show all" is pressed,
    // so the card stays about as tall as Reservation trends.
    const showUtilizationTables = () => {
        const listEl = document.getElementById("utilization-tables");
        const toggle = document.getElementById("utilization-toggle");
        const shown = showAllTables ? utilizationTables : utilizationTables.slice(0, TABLES_SHOWN);

        listEl.innerHTML = shown.map(t => {
            const percent = toPercent(t.rate).toFixed(1);
            const details = `${t.usedHours} h in use · ${plural(t.visits, "visit")}`;
            return `
                <li class="utilization-row" title="${AdminUI.escapeHtml(`Table ${t.id}: ${percent}% · ${details}`)}">
                    <span class="fw-medium text-truncate">${AdminUI.escapeHtml(t.id)}</span>
                    <span class="text-body-secondary">${plural(t.capacity, "seat")}</span>
                    <span class="utilization-track" aria-hidden="true"><span class="utilization-fill" style="width: ${Math.min(percent, 100)}%"></span></span>
                    <span class="fw-semibold text-end">${percent}%</span>
                    <span class="visually-hidden">, ${details}</span>
                </li>`;
        }).join("");

        const hidden = utilizationTables.length - TABLES_SHOWN;
        toggle.classList.toggle("d-none", hidden <= 0);
        toggle.textContent = showAllTables ? "Show fewer" : `Show all ${utilizationTables.length} tables`;
        toggle.setAttribute("aria-expanded", String(showAllTables));
    };

    // Long ranges are grouped by week (starting Monday), so the chart stays readable.
    const groupByWeek = (days) => {
        const weeks = new Map();
        for (const day of days) {
            const date = new Date(`${day.date}T00:00:00`);
            const monday = addDays(day.date, -((date.getDay() + 6) % 7));
            const week = weeks.get(monday) ?? { date: monday, count: 0, guests: 0 };
            week.count += day.count;
            week.guests += day.guests;
            weeks.set(monday, week);
        }
        return [...weeks.values()];
    };

    const showReservationsView = (view) => {
        const series = view === "slot" ? reservationsBySlot : reservationsByDate;
        reservationsChart.updateOptions({
            series: [{ name: "Bookings", data: series.bookings }],
            xaxis: { categories: series.labels },
            tooltip: { y: { formatter: (value, { dataPointIndex }) => `${plural(value, "booking")} · ${plural(series.guests[dataPointIndex], "guest")}` } }
        });

        for (const [button, active] of [[bookingsByDateButton, view === "date"], [bookingsBySlotButton, view === "slot"]]) {
            button.classList.toggle("active", active);
            button.classList.toggle("btn-dark", active);
        }
    };

    const showReservationTrends = (data) => {
        const trends = data.reservationTrends;
        const comparison = comparisonFor(data);

        document.getElementById("res-bookings").textContent = trends.bookings;
        document.getElementById("res-guests").textContent = trends.guests;
        document.getElementById("res-cancellation").textContent = `${trends.cancellationRate}%`;
        document.getElementById("res-cancellation").title = trends.cancelled
            ? `${plural(trends.cancelled, "booking")} cancelled, including ${plural(trends.noShows, "no-show")}`
            : "";
        document.getElementById("res-lead").textContent = trends.averageLeadDays === null ? "-" : `${trends.averageLeadDays} days`;

        setDelta("res-bookings-delta", trends.bookings, trends.previousBookings, String, comparison);
        setDelta("res-guests-delta", trends.guests, trends.previousGuests, String, comparison);
        setDelta("res-cancellation-delta", trends.cancellationRate, trends.previousCancellationRate,
            (points) => `${points.toFixed(1)} pts`, comparison, { lowerIsBetter: true });
        if (trends.averageLeadDays === null || trends.previousAverageLeadDays === null) {
            const leadDelta = document.getElementById("res-lead-delta");
            leadDelta.className = "text-body-secondary";
            leadDelta.innerHTML = "&nbsp;";
        } else {
            setDelta("res-lead-delta", trends.averageLeadDays, trends.previousAverageLeadDays,
                (days) => `${days.toFixed(1)} days`, comparison, { neutral: true });
        }

        const byWeek = data.reservationsByDay.length > 62;
        const dateRows = byWeek ? groupByWeek(data.reservationsByDay) : data.reservationsByDay;
        reservationsByDate = {
            labels: dateRows.map(d => byWeek ? `Wk of ${dayLabel(d.date)}` : dayLabel(d.date)),
            bookings: dateRows.map(d => d.count),
            guests: dateRows.map(d => d.guests)
        };
        reservationsBySlot = {
            labels: trends.slots.map(s => new Date(`2000-01-01T${s.time}:00`).toLocaleTimeString("en-MY", { hour: "numeric", minute: "2-digit" })),
            bookings: trends.slots.map(s => s.bookings),
            guests: trends.slots.map(s => s.guests)
        };

        if (reservationsChart) reservationsChart.destroy();
        reservationsChart = new ApexCharts(document.querySelector("#chart-reservations"), {
            chart: baseChart("bar"),
            series: [{ name: "Bookings", data: reservationsByDate.bookings }],
            colors: [INK],
            xaxis: { categories: reservationsByDate.labels },
            yaxis: { forceNiceScale: true, labels: { formatter: (value) => Math.round(value) } },
            plotOptions: { bar: { borderRadius: 3, columnWidth: "55%" } },
            grid: { borderColor: "rgba(127, 127, 127, 0.2)", strokeDashArray: 4 },
            dataLabels: { enabled: false },
            noData: { text: "No bookings in this date range." }
        });
        reservationsChart.render();
        showReservationsView(bookingsBySlotButton.classList.contains("active") ? "slot" : "date");
    };

    // A weekday x hour grid shaded by the average number of tables in use, from dining sessions.
    // The busiest cell is outlined; hours that haven't happened yet (later today) are left blank.
    const showPeakHours = (data) => {
        const peakHours = data.peakHours;
        const tableEl = document.getElementById("peak-heatmap");
        const summaryEl = document.getElementById("peak-summary");
        const caption = tableEl.querySelector("caption").outerHTML;
        const rows = peakHours.rows;

        if (peakHours.tableCount === 0) {
            tableEl.innerHTML = caption;
            summaryEl.className = "small text-body-secondary mb-3";
            summaryEl.textContent = "Add tables to see peak hours.";
            return;
        }

        let peak = null;
        for (const row of rows) {
            for (const cell of row.hours) {
                if (cell.tables > 0 && (!peak || cell.tables > peak.tables)) {
                    peak = { day: row.day, hour: cell.hour, tables: cell.tables };
                }
            }
        }

        const cellHtml = (day, cell) => {
            const where = `${AdminUI.DAY_NAMES[day]} ${hourLabel(cell.hour)}`;
            if (cell.tables === null) {
                return `<td class="upcoming" title="${where}: not yet"><span class="visually-hidden">${where}: not yet</span></td>`;
            }
            const share = Math.min(cell.tables / peakHours.tableCount, 1);
            const level = cell.tables > 0 ? 0.08 + share * 0.92 : 0;
            const isPeak = peak && peak.day === day && peak.hour === cell.hour;
            const text = `${where}: ${cell.tables} of ${peakHours.tableCount} tables in use (${toPercent(share)}%)`;
            return `<td class="${isPeak ? "peak" : ""}" style="--level: ${level.toFixed(2)}" title="${text}"><span class="visually-hidden">${text}</span></td>`;
        };

        const hours = rows[0].hours.map(cell => cell.hour);
        tableEl.innerHTML = caption
            + `<thead><tr><th scope="col"><span class="visually-hidden">Day</span></th>${hours.map(hour => `<th scope="col">${hourLabel(hour)}</th>`).join("")}</tr></thead>`
            + `<tbody>${rows.map(row => `<tr><th scope="row">${AdminUI.DAY_NAMES[row.day].slice(0, 3)}</th>${row.hours.map(cell => cellHtml(row.day, cell)).join("")}</tr>`).join("")}</tbody>`;

        summaryEl.className = "small mb-3";
        summaryEl.innerHTML = peak
            ? `Busiest: <span class="fw-semibold">${AdminUI.DAY_NAMES[peak.day]}, ${hourLabel(peak.hour)}</span>`
                + ` <span class="text-body-secondary">· on average ${peak.tables} of ${peakHours.tableCount} tables in use</span>`
            : `<span class="text-body-secondary">No tables were in use in this date range.</span>`;
    };

    const showRecentOrders = (data) => {
        recentOrdersEl.innerHTML = data.recentOrders.length
            ? data.recentOrders.map(o => `
                <tr>
                    <td class="fw-semibold ps-3">#${AdminUI.escapeHtml(o.id)}</td>
                    <td class="text-truncate" title="${AdminUI.escapeHtml(o.customer ?? "Walk-in")}">${AdminUI.escapeHtml(o.customer ?? "Walk-in")}</td>
                    <td>${AdminUI.escapeHtml(o.table ?? "-")}</td>
                    <td>${o.itemCount}</td>
                    <td class="text-nowrap">${AdminUI.formatTime(o.createdAt)}</td>
                    <td>${AdminUI.statusBadge(o.status)}</td>
                    <td class="text-end text-nowrap fw-semibold pe-3">${AdminUI.formatCurrency(o.total)}</td>
                </tr>`).join("")
            : AdminUI.tableMessageRow(7, "No orders yet.");
    };

    // The quick range that matches the dates is shown as a dark pill.
    const showSelectedRange = (range) => {
        selectedRange = range;
        for (const button of rangeForm.querySelectorAll("[data-range]")) {
            button.classList.toggle("btn-dark", button.dataset.range === range);
        }
    };

    const loadDashboard = () => {
        if (toInput.value < fromInput.value) {
            AdminUI.showToast("The To date must be on or after the From date.", "danger");
            return;
        }

        const fromText = AdminUI.formatDate(`${fromInput.value}T00:00:00`);
        const toText = AdminUI.formatDate(`${toInput.value}T00:00:00`);
        document.getElementById("range-label").textContent = fromText === toText ? fromText : `${fromText} - ${toText}`;
        document.getElementById("export-range").textContent = fromText === toText ? fromText : `${fromText} - ${toText}`;

        latestRequest++;
        const thisRequest = latestRequest;

        // "This month" is compared with the whole previous month; every other range with the days just before it.
        const query = { from: fromInput.value, to: toInput.value };
        if (selectedRange === "month") query.compare = "month";

        AdminUI.get("Stats", query)
            .then(data => {
                if (thisRequest !== latestRequest) return;
                showSummary(data);
                showRevenueChart(data);
                showOrdersChart(data);
                showTopItemsChart(data);
                showUtilization(data);
                showReservationTrends(data);
                showPeakHours(data);
                showRecentOrders(data);
            })
            .catch(error => {
                if (thisRequest !== latestRequest) return;
                recentOrdersEl.innerHTML = AdminUI.tableMessageRow(7, error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    // Downloads the report for the dates on screen. Fetched rather than linked, so a bad range shows
    // the server's message in a toast instead of opening a page of JSON.
    const exportReport = async (format) => {
        if (toInput.value < fromInput.value) {
            AdminUI.showToast("The To date must be on or after the From date.", "danger");
            return;
        }

        const url = new URL(window.location.pathname, window.location.origin);
        url.search = new URLSearchParams({ handler: "Export", from: fromInput.value, to: toInput.value, format }).toString();

        AdminUI.setBusy(exportButton, true);
        try {
            let response;
            try {
                response = await fetch(url, { cache: "no-store" });
            } catch {
                throw new Error("Can't reach the server. Check your connection and try again.");
            }
            if (!response.ok) {
                const data = await response.json().catch(() => null);
                throw new Error(data?.error ?? "The report could not be made. Please try again.");
            }

            const fileName = /filename="?([^";]+)"?/.exec(response.headers.get("Content-Disposition") ?? "")?.[1]
                ?? `analytics.${format}`;
            const link = document.createElement("a");
            link.href = URL.createObjectURL(await response.blob());
            link.download = fileName;
            document.body.append(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(link.href), 1000);
            AdminUI.showToast(`${format.toUpperCase()} report downloaded`);
        } catch (error) {
            AdminUI.showError(error);
        } finally {
            AdminUI.setBusy(exportButton, false);
        }
    };

    document.querySelector(".export-menu").addEventListener("click", (e) => {
        const option = e.target.closest("[data-export]");
        if (option) exportReport(option.dataset.export);
    });

    byDayButton.addEventListener("click", () => {
        if (ordersChart) showOrdersView("day");
    });

    document.getElementById("utilization-toggle").addEventListener("click", () => {
        showAllTables = !showAllTables;
        showUtilizationTables();
    });

    bookingsByDateButton.addEventListener("click", () => {
        if (reservationsChart) showReservationsView("date");
    });

    bookingsBySlotButton.addEventListener("click", () => {
        if (reservationsChart) showReservationsView("slot");
    });

    byHourButton.addEventListener("click", () => {
        if (ordersChart) showOrdersView("hour");
    });

    rangeForm.addEventListener("submit", (e) => {
        e.preventDefault();
        loadDashboard();
    });

    for (const input of [fromInput, toInput]) {
        input.addEventListener("change", () => showSelectedRange(null));
    }

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
        fromInput.dispatchEvent(new Event("change"));
        toInput.dispatchEvent(new Event("change"));
        showSelectedRange(button.dataset.range);
        loadDashboard();
    });

    showSelectedRange("week");
    recentOrdersEl.innerHTML = AdminUI.tableMessageRow(7, "Loading...");
    loadDashboard();
})();
