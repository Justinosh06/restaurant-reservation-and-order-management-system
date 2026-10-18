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
                ? "

Their RM20 deposit will be refunded to their card."
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
