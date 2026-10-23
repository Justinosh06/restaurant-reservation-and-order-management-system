// Notification bell in the customer navigation bar (Pages/Shared/_LandingNav.cshtml).
// Opening it marks every announcement as read, so the red count goes away.
// The red dots stay until the next page load, so the customer can still tell which ones were new.
(function () {
    const bell = document.getElementById("customer-bell");
    const count = document.getElementById("customer-bell-count");
    if (!bell || !count) return;

    bell.addEventListener("shown.bs.dropdown", async function () {
        const token = bell.parentElement.querySelector("input[name='__RequestVerificationToken']")?.value ?? "";
        try {
            const response = await fetch("/Customer/Notifications?handler=Seen", {
                method: "POST",
                headers: { "RequestVerificationToken": token }
            });
            if (!response.ok) return;

            count.remove();
            bell.setAttribute("aria-label", "Notifications");
        } catch {
            // Not marked as read; the count simply shows again on the next page.
        }
    }, { once: true });
})();
