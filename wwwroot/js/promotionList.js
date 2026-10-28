// Customer Promotions page (Pages/Customer/Promotions.cshtml): clicking a promotion box opens its poster,
// the same one the landing page pops up. The close button, Esc or a click outside the poster closes it.
// Unlike the landing pop-up, nothing is remembered: a promotion can be opened here as often as the customer likes.
(() => {
    const viewer = document.getElementById("promo-viewer");
    if (!viewer) {
        return;
    }

    // "2d 10:25:50" until the promotion ends; "10:25:50" on its last day.
    const pad = value => String(value).padStart(2, "0");
    const formatTimeLeft = milliseconds => {
        const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
        const days = Math.floor(totalSeconds / 86400);
        const clock = `${pad(Math.floor(totalSeconds % 86400 / 3600))}:${pad(Math.floor(totalSeconds % 3600 / 60))}:${pad(totalSeconds % 60)}`;
        return days > 0 ? `${days}d ${clock}` : clock;
    };

    let openCard = null;
    let opener = null;
    let timer = null;

    const tick = () => {
        const timeLeft = openCard?.querySelector(".promo-time-left");
        if (timeLeft) {
            timeLeft.textContent = formatTimeLeft(new Date(timeLeft.dataset.ends) - new Date());
        }
    };

    const open = (id, button) => {
        openCard = viewer.querySelector(`.promo-card[data-promo-id="${CSS.escape(id)}"]`);
        if (!openCard) {
            return;
        }

        opener = button;
        viewer.querySelectorAll(".promo-card").forEach(card => {
            card.hidden = card !== openCard;
        });
        viewer.hidden = false;
        document.body.classList.add("promo-open");
        tick();
        clearInterval(timer);
        timer = setInterval(tick, 1000);
        openCard.querySelector(".promo-close")?.focus();
    };

    const close = () => {
        viewer.hidden = true;
        document.body.classList.remove("promo-open");
        clearInterval(timer);
        openCard = null;
        opener?.focus();
    };

    document.querySelectorAll("[data-promo-open]").forEach(button => {
        button.addEventListener("click", () => open(button.dataset.promoOpen, button));
    });
    viewer.querySelectorAll(".promo-close").forEach(button => button.addEventListener("click", close));
    viewer.addEventListener("click", event => {
        if (event.target === viewer) {
            close();
        }
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && !viewer.hidden) {
            close();
        }
    });
})();
