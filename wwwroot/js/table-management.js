document.addEventListener("DOMContentLoaded", function () {
    const app = document.querySelector(".service-app");
    const panel = document.getElementById("tableDetailsPanel");
    const backdrop = document.getElementById("panelBackdrop");
    const closeButton = document.getElementById("closePanelButton");
    const manageLink = document.getElementById("manageTableReservations");
    const reservationItems = Array.from(
        document.querySelectorAll(".table-reservation")
    );
    const noReservations = document.getElementById("noTableReservations");
    const reservationCount = document.getElementById("tableReservationCount");
    const removeForm = document.getElementById("removeTableForm");
    const removeTableId = document.getElementById("removeTableId");
    const addPanel = document.getElementById("addTablePanel");
    const addButton = document.getElementById("addTableButton");
    const closeAddButton = document.getElementById("closeAddTableButton");

    if (!app || !panel || !backdrop || !closeButton || !manageLink
        || !removeForm || !addPanel || !addButton || !closeAddButton) {
        console.error("Table management page is missing required elements.");
        return;
    }

    function openTable(id) {
        const table = document.querySelector(
            '.floor-table[data-table-id="' + CSS.escape(id) + '"]'
        );
        if (!table) {
            console.error("Cannot open unknown table:", id);
            return;
        }

        document.getElementById("panelTitle").textContent = "Table " + id;
        document.getElementById("panelCapacity").textContent =
            table.dataset.capacity + " seats";
        manageLink.href =
            "/Admin/Reservations?tableId=" + encodeURIComponent(id);

        // This table's order QR code, drawn by qrcode.js from the menu link the server worked out.
        const menuUrl = table.dataset.menuUrl;
        drawTableQr(id, menuUrl);
        document.getElementById("tableQrLink").href = menuUrl;
        document.getElementById("tableQrLink").textContent = menuUrl;
        document.getElementById("tableQrOpen").href = menuUrl;
        removeTableId.value = id;
        removeTableId.dataset.unseatCount = table.dataset.unseatCount || "0";
        removeTableId.dataset.sharedCount = table.dataset.sharedCount || "0";
        removeTableId.dataset.seatedNow = table.dataset.seatedNow || "false";

        let visibleCount = 0;
        reservationItems.forEach(function (item) {
            const visible = item.dataset.tableReservation.split(" ").includes(id);
            item.hidden = !visible;
            if (visible) {
                visibleCount++;
            }
        });

        reservationCount.textContent =
            visibleCount + (visibleCount === 1 ? " booking" : " bookings");
        noReservations.hidden = visibleCount !== 0;

        panel.classList.add("open");
        backdrop.classList.add("open");
        panel.setAttribute("aria-hidden", "false");
        closeButton.focus();
    }

    // Table QR codes (qrcode.js): drawn into #tableQrCode whenever a table's panel opens.
    const qrBox = document.getElementById("tableQrCode");
    let qrTableId = null;

    function drawTableQr(id, menuUrl) {
        qrTableId = id;
        qrBox.replaceChildren();
        qrBox.setAttribute("aria-label", "QR code for table " + id + ": " + menuUrl);
        if (typeof QRCode === "undefined") {
            qrBox.textContent = "QR code unavailable";
            console.error("qrcode.js did not load.");
            return;
        }
        new QRCode(qrBox, {
            text: menuUrl,
            width: 360,
            height: 360,
            colorDark: "#000000",
            colorLight: "#ffffff",
            correctLevel: QRCode.CorrectLevel.M
        });
    }

    // The drawn code as a PNG (qrcode.js draws on a canvas), for Download and Print.
    function tableQrPng() {
        const canvas = qrBox.querySelector("canvas");
        return canvas ? canvas.toDataURL("image/png") : null;
    }

    document.getElementById("tableQrDownload").addEventListener("click", function () {
        const png = tableQrPng();
        if (!png) {
            return;
        }
        const link = document.createElement("a");
        link.href = png;
        link.download = "Table-" + qrTableId + "-QR.png";
        document.body.appendChild(link);
        link.click();
        link.remove();
    });

    // Prints a card with the table's QR code, for a table stand.
    document.getElementById("tableQrPrint").addEventListener("click", function () {
        const id = qrTableId;
        const image = { src: tableQrPng() };
        if (!image.src) {
            return;
        }
        const card = window.open("", "_blank", "width=420,height=560");
        if (!card) {
            AppDialog.alert({ title: "Pop-up blocked", message: "Allow pop-ups for this site to print the QR code, or use Download.", icon: "fa-print" });
            return;
        }
        const label = document.createElement("div");
        label.textContent = id;
        card.document.write(
            "<!doctype html><title>Table " + label.innerHTML + " QR</title>" +
            "<style>body{font-family:Inter,system-ui,sans-serif;text-align:center;padding:32px}" +
            "h1{margin:0 0 4px;font-size:34px}p{margin:0 0 18px;color:#555;font-size:16px}" +
            "img{width:280px;height:280px}small{display:block;margin-top:12px;color:#888}</style>" +
            "<h1>Table " + label.innerHTML + "</h1><p>Scan to see the menu and order</p>" +
            "<img src=\"" + image.src + "\" onload=\"window.print()\" alt=\"\">" +
            "<small>" + document.getElementById("tableQrLink").textContent.replace(/</g, "&lt;") + "</small>");
        card.document.close();
    });

    function openAddPanel() {
        addPanel.classList.add("open");
        backdrop.classList.add("open");
        addPanel.setAttribute("aria-hidden", "false");
        const checked = addPanel.querySelector("input[name=seats]:checked");
        if (checked) {
            checked.focus();
        }
    }

    // Closes whichever side panel is open.
    function closePanel() {
        [panel, addPanel].forEach(function (item) {
            item.classList.remove("open");
            item.setAttribute("aria-hidden", "true");
        });
        backdrop.classList.remove("open");
    }

    // Hovering a booking in the sidebar lights up its tables on the floor plan.
    const floorTables = Array.from(document.querySelectorAll(".floor-table"));
    document.querySelectorAll(".checkin-item").forEach(function (item) {
        const ids = item.dataset.tables ? item.dataset.tables.split(" ") : [];
        const spotlight = function (on) {
            floorTables.forEach(function (table) {
                table.classList.toggle("spotlight", on && ids.includes(table.dataset.tableId));
            });
        };
        item.addEventListener("mouseenter", function () { spotlight(true); });
        item.addEventListener("mouseleave", function () { spotlight(false); });
    });

    document.querySelectorAll(".undo-checkin-form").forEach(function (form) {
        AppDialog.guardSubmit(form, {
            title: "Undo this check-in?",
            message: "The booking goes back to waiting for its guests and the timer stops.",
            confirmText: "Undo check-in",
            cancelText: "Keep",
            icon: "fa-rotate-left"
        });
    });

    floorTables.forEach(function (button) {
        button.addEventListener("click", function () {
            openTable(button.dataset.tableId);
        });
    });

    closeButton.addEventListener("click", closePanel);
    closeAddButton.addEventListener("click", closePanel);
    addButton.addEventListener("click", openAddPanel);
    backdrop.addEventListener("click", closePanel);
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && backdrop.classList.contains("open")) {
            closePanel();
        }
    });

    AppDialog.guardSubmit(removeForm, function () {
        if (removeTableId.dataset.seatedNow === "true") {
            AppDialog.alert({
                title: "Guests are at " + removeTableId.value,
                message: "Someone is checked in at this table right now. Remove it once they have checked out.",
                confirmText: "OK",
                icon: "fa-utensils"
            });
            return null;
        }

        // Bookings are never deleted with a table; they only lose it.
        const unseat = Number(removeTableId.dataset.unseatCount);
        const shared = Number(removeTableId.dataset.sharedCount);
        const notes = [];
        if (unseat > 0) {
            notes.push(unseat + (unseat === 1 ? " upcoming booking goes" : " upcoming bookings go")
                + " back to Pending so you can seat " + (unseat === 1 ? "it" : "them") + " elsewhere.");
        }
        if (shared > 0) {
            notes.push(shared + (shared === 1 ? " booking also uses" : " bookings also use")
                + " other tables and keeps them.");
        }
        return {
            title: "Remove table " + removeTableId.value + "?",
            message: notes.length > 0 ? notes.join("\n") : "No upcoming bookings use this table.",
            confirmText: "Remove table",
            cancelText: "Keep table",
            danger: true,
            icon: "fa-trash"
        };
    });

    if (app.dataset.selectedTable) {
        openTable(app.dataset.selectedTable);
    }
});

// Seated timers: count up from each check-in time, once a second (H:MM:SS, or M:SS under an hour).
(function () {
    const timers = Array.from(document.querySelectorAll(".seated-timer[data-since]"));
    if (timers.length === 0) {
        return;
    }

    function format(totalSeconds) {
        const seconds = Math.max(0, Math.floor(totalSeconds));
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = String(seconds % 60).padStart(2, "0");
        return h > 0 ? h + ":" + String(m).padStart(2, "0") + ":" + s : m + ":" + s;
    }

    // Matches ReservationSummary.SlotLength: after this the table is overdue (timer turns orange).
    const slotSeconds = 2 * 60 * 60;

    function tick() {
        const now = Date.now();
        timers.forEach(function (timer) {
            const since = Date.parse(timer.dataset.since);
            if (!Number.isNaN(since)) {
                const seconds = (now - since) / 1000;
                timer.textContent = format(seconds);
                timer.classList.toggle("overdue", seconds >= slotSeconds);
                timer.title = seconds >= slotSeconds ? "Over the 2-hour booking time" : "Time since check-in";
            }
        });
    }

    tick();
    setInterval(tick, 1000);
})();

// Check out: confirm before freeing the table. Waits for the page to load because the layout
// loads the pop-up script (confirm-dialog.js) after this one.
document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll(".checkout-form").forEach(function (form) {
        AppDialog.guardSubmit(form, {
            title: "Check out " + form.dataset.name + "?",
            message: "Their table becomes free and the booking is marked Completed.",
            confirmText: "Check out",
            cancelText: "Not yet",
            icon: "fa-right-from-bracket"
        });
    });
});
