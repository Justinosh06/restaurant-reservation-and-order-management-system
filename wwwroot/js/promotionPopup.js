// Promotion pop-up (see Pages/Shared/_PromotionPopup.cshtml).
// Shows the promotions one at a time; the arrows (or the Left/Right keys, or a swipe) move between them, and the
// close button (or Esc) closes the whole pop-up. Closing it dismisses every promotion in it, including ones the
// visitor didn't move to, and their ids are remembered in sessionStorage so they do not pop up again in this tab
// (for example after logging in). A promotion added later still pops up, because only those ids are remembered.
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

    const markSeen = ids => {
        try {
            const seen = readSeen();
            sessionStorage.setItem(storageKey, JSON.stringify([...new Set([...seen, ...ids])]));
        } catch {
            // Not saved: the promotion shows again next time, which is harmless.
        }
    };

    // Promotions already seen are taken out, so the window is sized only by the ones still to show.
    const seen = readSeen();
    const cards = Array.from(popup.querySelectorAll(".promo-card")).filter(card => {
        if (seen.includes(card.dataset.promoId)) {
            card.remove();
            return false;
        }
        return true;
    });
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

    const stage = popup.querySelector(".promo-stage");
    const closeButton = popup.querySelector(".promo-close-all");
    const prevButton = popup.querySelector(".promo-prev");
    const nextButton = popup.querySelector(".promo-next");
    const position = popup.querySelector(".promo-position");
    const several = cards.length > 1;
    popup.querySelector(".promo-pager").hidden = !several;
    closeButton.setAttribute("aria-label", several ? "Close all promotions" : "Close promotion");

    // The window is the dialog here; each poster is just its content, with no close button of its own.
    cards.forEach(card => {
        card.removeAttribute("role");
        card.removeAttribute("aria-modal");
        card.querySelector(".promo-close")?.remove();
    });

    let current = 0;
    let timer = null;

    const tick = () => {
        const timeLeft = cards[current]?.querySelector(".promo-time-left");
        if (timeLeft) {
            timeLeft.textContent = formatTimeLeft(new Date(timeLeft.dataset.ends) - new Date());
        }
    };

    // Moving between posters: the old one slides out and fades as the new one slides in from the side being
    // moved towards. Skipped for visitors who ask their device for reduced motion.
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    const slideDistance = 32;
    let running = [];

    const slide = (outgoing, incoming, direction) => {
        // A quick second click finishes the previous slide at once instead of piling animations up.
        running.forEach(animation => animation.finish());
        const timing = { duration: 280, easing: "cubic-bezier(0.22, 1, 0.36, 1)" };
        const leave = outgoing.animate([
            { opacity: 1, transform: "translateX(0)" },
            { opacity: 0, transform: `translateX(${-direction * slideDistance}px)` }
        ], { ...timing, duration: 200 });
        const enter = incoming.animate([
            { opacity: 0, transform: `translateX(${direction * slideDistance}px)` },
            { opacity: 1, transform: "translateX(0)" }
        ], timing);
        running = [leave, enter];

        // The old poster stays visible until it has faded out, then joins the other invisible ones.
        leave.onfinish = () => outgoing.classList.toggle("promo-offstage", outgoing !== cards[current]);
    };

    // focusTarget keeps keyboard focus on the arrow that was pressed, so it can be pressed again.
    const show = (index, focusTarget) => {
        const previous = cards[current];
        const direction = Math.sign(index - current);
        current = index;
        const animate = direction !== 0 && !reduceMotion.matches;

        // The others stay in place but invisible (and inert, so they can't be reached by keyboard or screen
        // reader), keeping the window at the tallest poster's height.
        cards.forEach((card, i) => {
            card.hidden = false;
            card.classList.toggle("promo-offstage", i !== index && !(animate && card === previous));
            card.inert = i !== index;
        });
        if (animate) {
            slide(previous, cards[index], direction);
        }

        stage.setAttribute("aria-labelledby", cards[index].querySelector(".promo-title").id);
        position.textContent = `${index + 1} of ${cards.length}`;
        prevButton.disabled = index === 0;
        nextButton.disabled = index === cards.length - 1;

        tick();
        (focusTarget && !focusTarget.disabled ? focusTarget : closeButton).focus();
    };

    const move = (step, focusTarget) => {
        const index = current + step;
        if (index >= 0 && index < cards.length) {
            show(index, focusTarget);
        }
    };

    // Closes every promotion at once and remembers all of them for this tab.
    const closeAll = () => {
        markSeen(cards.map(card => card.dataset.promoId));
        clearInterval(timer);
        popup.hidden = true;
        document.body.classList.remove("promo-open");
    };

    closeButton.addEventListener("click", closeAll);
    prevButton.addEventListener("click", () => move(-1, prevButton));
    nextButton.addEventListener("click", () => move(1, nextButton));

    document.addEventListener("keydown", event => {
        if (popup.hidden) {
            return;
        }
        if (event.key === "Escape") {
            closeAll();
        } else if (event.key === "ArrowLeft") {
            move(-1);
        } else if (event.key === "ArrowRight") {
            move(1);
        }
    });

    // A sideways swipe on a phone moves between promotions; mostly-vertical drags are left to scroll the poster.
    let touchStart = null;
    popup.addEventListener("touchstart", event => {
        touchStart = event.touches.length === 1 ? event.touches[0] : null;
    }, { passive: true });
    popup.addEventListener("touchend", event => {
        if (!touchStart || !several) {
            return;
        }
        const dx = event.changedTouches[0].clientX - touchStart.clientX;
        const dy = event.changedTouches[0].clientY - touchStart.clientY;
        touchStart = null;
        if (Math.abs(dx) > 50 && Math.abs(dx) > Math.abs(dy) * 1.5) {
            move(dx < 0 ? 1 : -1);
        }
    });

    popup.hidden = false;
    document.body.classList.add("promo-open");
    show(current);
    timer = setInterval(tick, 1000);
})();
