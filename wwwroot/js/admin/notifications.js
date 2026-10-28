// Announcement bell in the admin sidebar (Pages/Shared/_AdminLayout.cshtml).
// Opening it marks every announcement as seen for this account, so the red count goes away.
// The red dots stay until the next page load, so you can still tell which ones were new.
(() => {
    const bell = document.getElementById("notification-bell");
    const count = document.getElementById("notification-count");
    if (!bell || !count) return;

    bell.addEventListener("shown.bs.dropdown", async () => {
        const token = document.querySelector("input[name='__RequestVerificationToken']")?.value ?? "";
        try {
            const response = await fetch("/Admin/Notifications?handler=Seen", {
                method: "POST",
                headers: { "RequestVerificationToken": token }
            });
            if (!response.ok) return;

            count.remove();
            document.getElementById("notification-unread")?.remove();
            bell.setAttribute("aria-label", "Announcements");
        } catch {
            // Not marked as seen; the count simply shows again on the next page.
        }
    }, { once: true });
})();
