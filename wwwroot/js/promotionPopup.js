// Promotion pop-up (see Pages/Shared/_PromotionPopup.cshtml).
// Shows the promotions one at a time; the close button (or Esc) closes one and shows the next. Each closed
// promotion is remembered in sessionStorage, so it does not pop up again in this tab (for example after
// logging in). A promotion added later still pops up, because only the ids that were closed are remembered.
(() => {
    const storageKey = "restaurant-promotions-seen";
    const popup = document.getElementById("promo-popup");
    if (!popup) {
        return;
    }

    // Storage can be unavailable (private mode, blocked site data); the pop-up then simply shows each time.
    const readSeen = () => {
        try {
            const seen = JSON.parse(sessionStorage.getItem(storageKey) ?? "[]");
            return Array.isArray(seen) ? seen : [];
        } catch {
            return [];
        }
    };

    const markSeen = id => {
        try {
            const seen = readSeen();
            if (!seen.includes(id)) {
                seen.push(id);
                sessionStorage.setItem(storageKey, JSON.stringify(seen));
            }
        } catch {
            // Not saved: the promotion shows again next time, which is harmless.
        }
    };

    const seen = readSeen();
    const cards = Array.from(popup.querySelectorAll(".promo-card"))
        .filter(card => !seen.includes(card.dataset.promoId));
    if (cards.length === 0) {
        return;
    }

    // "2d 10:25:50" until the promotion ends; "10:25:50" on its last day.
    const pad = value => String(value).padStart(2, "0");
    const formatTimeLeft = milliseconds => {
        const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
        const days = Math.floor(totalSeconds / 86400);
        const hours = Math.floor(totalSeconds % 86400 / 3600);
        const minutes = Math.floor(totalSeconds % 3600 / 60);
        const seconds = totalSeconds % 60;
        const clock = `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
        return days > 0 ? `${days}d ${clock}` : clock;
    };

    let current = 0;
    let timer = null;

    const tick = () => {
        const timeLeft = cards[current]?.querySelector(".promo-time-left");
        if (timeLeft) {
            timeLeft.textContent = formatTimeLeft(new Date(timeLeft.dataset.ends) - new Date());
        }
    };

    const show = index => {
        cards.forEach((card, i) => {
            card.hidden = i !== index;
        });

        const count = cards[index].querySelector(".promo-count");
        if (count) {
            count.textContent = cards.length > 1 ? ` · ${index + 1} of ${cards.length}` : "";
        }

        tick();
        cards[index].querySelector(".promo-close")?.focus();
    };

    const closeCurrent = () => {
        markSeen(cards[current].dataset.promoId);
        current++;

        if (current < cards.length) {
            show(current);
        } else {
            clearInterval(timer);
            popup.hidden = true;
            document.body.classList.remove("promo-open");
        }
    };

    cards.forEach(card => card.querySelector(".promo-close")?.addEventListener("click", closeCurrent));

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && !popup.hidden) {
            closeCurrent();
        }
    });

    popup.hidden = false;
    document.body.classList.add("promo-open");
    show(current);
    timer = setInterval(tick, 1000);
})();
