document.addEventListener("DOMContentLoaded", function () {
    const app = document.querySelector(".reservation-management-page");
    const panel = document.getElementById("reservationEditorPanel");
    const backdrop = document.getElementById("reservationFormBackdrop");
    const closeButton = document.getElementById("closeReservationForm");

    if (!app || !panel || !backdrop || !closeButton) {
        console.error("Reservation management page is missing required elements.");
        return;
    }

    function closeEditor() {
        panel.classList.remove("open");
        backdrop.classList.remove("open");
        panel.setAttribute("aria-hidden", "true");
    }

    closeButton.addEventListener("click", closeEditor);
    backdrop.addEventListener("click", closeEditor);

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && panel.classList.contains("open")) {
            closeEditor();
        }
    });

    document.querySelectorAll(".delete-reservation-form").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            if (!window.confirm("Delete this reservation? This cannot be undone.")) {
                event.preventDefault();
            }
        });
    });

    document.querySelectorAll(".cancel-reservation-form").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            const refund = form.dataset.deposit === "paid"
                ? "\n\nTheir RM20 deposit will be refunded to their card."
                : "";
            if (!window.confirm("Cancel this reservation? It will move to the Cancelled tab." + refund)) {
                event.preventDefault();
            }
        });
    });

    // Edit menus: one open at a time, closed by clicking elsewhere or pressing Escape.
    const menus = Array.from(document.querySelectorAll(".booking-menu"));
    menus.forEach(function (menu) {
        menu.addEventListener("toggle", function () {
            if (menu.open) {
                menus.forEach(function (other) {
                    if (other !== menu) {
                        other.open = false;
                    }
                });
            }
        });
    });
    document.addEventListener("click", function (event) {
        menus.forEach(function (menu) {
            if (menu.open && !menu.contains(event.target)) {
                menu.open = false;
            }
        });
    });
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            menus.forEach(function (menu) {
                menu.open = false;
            });
        }
    });

    // Assign table panel: tap free tables until the whole party fits, then save.
    const assignPanel = document.getElementById("assignPanel");
    const assignTitle = document.getElementById("assignTitle");
    const assignInfo = document.getElementById("assignInfo");
    const assignSummary = document.getElementById("assignSummary");
    const assignForm = document.getElementById("assignForm");
    const assignTableIds = document.getElementById("assignTableIds");
    const assignSave = document.getElementById("assignSave");
    const assignClear = document.getElementById("assignClear");
    const assignTables = Array.from(document.querySelectorAll(".assign-table"));
    let assigning = null;
    let picked = [];

    function idList(value) {
        return value ? value.split(" ") : [];
    }

    function showPicked() {
        const party = Number(assigning.dataset.party);
        const free = idList(assigning.dataset.free);
        const current = idList(assigning.dataset.tables);
        const suggested = idList(assigning.dataset.suggested);
        let seats = 0;

        assignTables.forEach(function (table) {
            const id = table.dataset.tableId;
            const isPicked = picked.includes(id);
            const isFree = free.includes(id);
            if (isPicked) {
                seats += Number(table.dataset.capacity);
            }
            table.classList.toggle("picked", isPicked);
            table.disabled = !isFree;
            table.setAttribute("aria-pressed", isPicked ? "true" : "false");
            table.querySelector(".assign-flag").textContent = !isFree ? "Booked"
                : current.includes(id) ? "Current"
                : suggested.includes(id) && current.length === 0 ? "Suggested"
                : "";
        });

        const enough = picked.length > 0 && seats >= party;
        const unchanged = picked.length === current.length && picked.every(function (id) {
            return current.includes(id);
        });
        assignSummary.classList.toggle("short", picked.length > 0 && !enough);
        assignSummary.textContent = picked.length === 0
            ? (free.length === 0 ? "No table is free at this time." : "No table picked yet.")
            : seats + " seats for " + party + " guests · " + picked.join(" + ")
                + (enough ? "" : " · not enough, pick another table");
        assignSave.disabled = !enough || unchanged;
        assignClear.hidden = current.length === 0;
    }

    function openAssign(button) {
        assigning = button;
        const current = idList(button.dataset.tables);
        picked = current.length > 0 ? current : idList(button.dataset.suggested);
        assignTitle.textContent = button.dataset.name;
        assignInfo.textContent = button.dataset.party + " guests · " + button.dataset.time;
        assignPanel.querySelector(".eyebrow").textContent = current.length > 0 ? "MOVE TABLE" : "ASSIGN TABLE";
        showPicked();

        menus.forEach(function (menu) {
            menu.open = false;
        });
        assignPanel.classList.add("open");
        backdrop.classList.add("open");
        assignPanel.setAttribute("aria-hidden", "false");
        document.getElementById("closeAssignPanel").focus();
    }

    function closeAssign() {
        assignPanel.classList.remove("open");
        assignPanel.setAttribute("aria-hidden", "true");
        if (!panel.classList.contains("open")) {
            backdrop.classList.remove("open");
        }
        assigning = null;
    }

    function submitAssign(tableIds) {
        postAssignment(assigning.dataset.reservationId, tableIds);
    }

    function postAssignment(reservationId, tableIds) {
        document.getElementById("assignReservationId").value = reservationId;
        assignTableIds.replaceChildren();
        tableIds.forEach(function (id) {
            const input = document.createElement("input");
            input.type = "hidden";
            input.name = "tableIds";
            input.value = id;
            assignTableIds.appendChild(input);
        });
        assignForm.submit();
    }

    document.querySelectorAll(".assign-open").forEach(function (button) {
        button.addEventListener("click", function () {
            openAssign(button);
        });
    });

    assignTables.forEach(function (table) {
        table.addEventListener("click", function () {
            const id = table.dataset.tableId;
            picked = picked.includes(id)
                ? picked.filter(function (other) { return other !== id; })
                : picked.concat(id);
            showPicked();
        });
    });

    assignForm.addEventListener("submit", function (event) {
        event.preventDefault();
        if (assigning && !assignSave.disabled) {
            submitAssign(picked);
        }
    });
    assignClear.addEventListener("click", function () {
        if (assigning && window.confirm("Take this reservation off its tables? It goes back to Pending.")) {
            submitAssign([]);
        }
    });
    document.getElementById("closeAssignPanel").addEventListener("click", closeAssign);
    backdrop.addEventListener("click", closeAssign);
    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && assigning) {
            closeAssign();
        }
    });

    // Assign table (Pending bookings): the floor plan, like Table Management, in a pop-up.
    // The suggested tables start picked; tap free tables to change them, then save.
    const floorDialog = document.getElementById("floorAssign");
    if (floorDialog) {
        const banner = document.getElementById("pendingBanner");
        const bannerText = document.getElementById("pendingText");
        const saveButton = document.getElementById("pendingSave");
        const floorTables = Array.from(floorDialog.querySelectorAll(".floor-table"));
        let selected = null;
        let chosen = [];

        const showChosen = function () {
            const free = idList(selected.dataset.free);
            const party = Number(selected.dataset.party);
            let seats = 0;
            floorTables.forEach(function (table) {
                const id = table.dataset.tableId;
                const isPicked = chosen.includes(id);
                if (isPicked) {
                    seats += Number(table.dataset.capacity);
                }
                table.classList.toggle("picked", isPicked);
                table.classList.toggle("fits", !isPicked && free.includes(id));
                table.classList.toggle("no-fit", !isPicked && !free.includes(id));
                table.setAttribute("aria-pressed", isPicked ? "true" : "false");
            });

            const enough = chosen.length > 0 && seats >= party;
            banner.classList.toggle("warning", chosen.length > 0 && !enough);
            bannerText.textContent = chosen.length === 0
                ? (free.length > 0
                    ? "Tap the tables to seat " + party + " guests."
                    : "No table is free at this time. Free one up or add a table.")
                : seats + " seats for " + party + " guests (" + chosen.join(" + ") + ")"
                    + (enough ? "" : ". Tap another table to add it.");
            saveButton.disabled = !enough;
        };

        const closeFloor = function () {
            if (!selected) {
                return;
            }
            selected = null;
            floorDialog.classList.remove("open");
            floorDialog.setAttribute("aria-hidden", "true");
            if (!panel.classList.contains("open")) {
                backdrop.classList.remove("open");
            }
        };

        document.querySelectorAll(".floor-assign-open").forEach(function (button) {
            button.addEventListener("click", function () {
                selected = button;
                chosen = idList(button.dataset.suggested);
                document.getElementById("floorAssignTitle").textContent = button.dataset.name;
                document.getElementById("floorAssignInfo").textContent =
                    button.dataset.party + " guests · " + button.dataset.time;
                menus.forEach(function (menu) {
                    menu.open = false;
                });
                showChosen();
                floorDialog.classList.add("open");
                floorDialog.setAttribute("aria-hidden", "false");
                backdrop.classList.add("open");
                document.getElementById("closeFloorAssign").focus();
            });
        });

        floorTables.forEach(function (table) {
            table.addEventListener("click", function () {
                const id = table.dataset.tableId;
                if (chosen.includes(id)) {
                    chosen = chosen.filter(function (other) { return other !== id; });
                } else if (idList(selected.dataset.free).includes(id)) {
                    chosen = chosen.concat(id);
                } else {
                    banner.classList.add("warning");
                    bannerText.textContent = id + " is already booked around " + selected.dataset.time + ". Pick another table.";
                    return;
                }
                showChosen();
            });
        });

        saveButton.addEventListener("click", function () {
            if (selected && !saveButton.disabled) {
                postAssignment(selected.dataset.reservationId, chosen);
            }
        });
        document.getElementById("pendingCancel").addEventListener("click", closeFloor);
        document.getElementById("closeFloorAssign").addEventListener("click", closeFloor);
        backdrop.addEventListener("click", closeFloor);
        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                closeFloor();
            }
        });
    }

    if (app.dataset.reopenForm === "true") {
        const form = app.dataset.formMode === "edit"
            ? document.getElementById("editReservationForm")
            : document.getElementById("createReservationForm");
        const firstInput = form.querySelector("input:not([type=hidden]), select");
        if (firstInput) {
            firstInput.focus();
        }
    }
});
