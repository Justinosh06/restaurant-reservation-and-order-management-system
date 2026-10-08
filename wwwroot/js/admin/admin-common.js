/*
 * admin-common.js - shared helpers for every admin page.
 *
 * Load this before the page's own script, e.g. in a page's Scripts section:
 *   <script src="~/js/admin/admin-common.js"></script>
 *   <script src="~/js/admin/orders.js"></script>
 *
 * It exposes one global object, AdminUI, with:
 *   - Formatting: escapeHtml, formatCurrency, formatDate, formatTime, currencyCode
 *   - Rendering:  statusBadge, tableMessageRow
 *   - Feedback:   showToast, showError, setBusy
 *   - Server:     get, post, postForm (call the current page's Razor Page handlers)
 *
 * Server calls go to the page's own PageModel handlers. For example,
 * AdminUI.post("Create", {...}) on /Admin/Staffs calls OnPostCreateAsync in
 * Pages/Admin/Staffs/Index.cshtml.cs. POSTs automatically send the anti-forgery
 * token that _AdminLayout.cshtml renders, so pages don't need to handle it.
 */
const AdminUI = (() => {
    // Always pass user-entered text through this before putting it into innerHTML.
    const escapeHtml = (value) => String(value ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");

    // The restaurant's currency comes from Restaurant Settings; _AdminLayout.cshtml puts it on <body>.
    const currencySymbol = document.body.dataset.currencySymbol || "RM";
    const currencyCode = document.body.dataset.currencyCode || "MYR";

    // Amounts are stored in cents in the database (e.g. 6800 -> "RM 68.00" or "$68.00").
    const formatCurrency = (amountInCents) => {
        const amount = (amountInCents / 100).toLocaleString("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const separator = /^[A-Za-z]+$/.test(currencySymbol) ? " " : "";
        return `${currencySymbol}${separator}${amount}`;
    };

    const formatDate = (date) =>
        new Date(date).toLocaleDateString("en-MY", { day: "numeric", month: "short", year: "numeric" });

    const formatTime = (date) =>
        new Date(date).toLocaleTimeString("en-MY", { hour: "2-digit", minute: "2-digit" });

    // Bootstrap badge colour for each status/role label. Add new labels here.
    const STATUS_BADGES = {
        Pending: "text-bg-warning",
        Preparing: "text-bg-primary",
        Served: "text-bg-success",
        Active: "text-bg-success",
        Scheduled: "text-bg-warning",
        Ended: "text-bg-secondary",
        Expired: "text-bg-secondary",
        Hidden: "text-bg-secondary",
        Administrator: "text-bg-dark",
        Staff: "bg-body-secondary text-body border"
    };

    const statusBadge = (status) =>
        `<span class="badge ${STATUS_BADGES[status] ?? "text-bg-secondary"}">${escapeHtml(status)}</span>`;

    // A single full-width table row for "Loading...", empty and error messages.
    const tableMessageRow = (colspan, message, tone = "text-muted") =>
        `<tr><td colspan="${colspan}" class="text-center ${tone} py-4">${escapeHtml(message)}</td></tr>`;

    // Small message in the bottom-right corner that hides itself after 3 seconds.
    let toastTimer;
    const showToast = (message, tone = "dark") => {
        let toast = document.getElementById("admin-toast");
        if (!toast) {
            toast = document.createElement("div");
            toast.id = "admin-toast";
            toast.style.zIndex = 2000;
            toast.setAttribute("role", "status");
            document.body.appendChild(toast);
        }
        toast.className = `alert alert-${tone} position-fixed bottom-0 end-0 m-3 shadow`;
        toast.textContent = message;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toast.classList.add("d-none"), 3000);
    };

    const showError = (error) => showToast(error.message, "danger");

    const antiForgeryToken = () =>
        document.querySelector("input[name='__RequestVerificationToken']")?.value ?? "";

    // Calls "<current page>?handler=<handler>" and returns the JSON response.
    // On failure it throws an Error whose message is the server's { error } text,
    // with .status (HTTP code) and .data (full response body) attached.
    const request = async (handler, { method = "GET", json, formData } = {}) => {
        const url = new URL(window.location.pathname, window.location.origin);
        url.searchParams.set("handler", handler);

        const headers = { "Accept": "application/json" };
        let body;
        if (json !== undefined) {
            headers["Content-Type"] = "application/json";
            body = JSON.stringify(json);
        } else if (formData) {
            body = formData;
        }
        if (method !== "GET") headers["RequestVerificationToken"] = antiForgeryToken();

        let response;
        try {
            response = await fetch(url, { method, headers, body });
        } catch {
            throw new Error("Can't reach the server. Check your connection and try again.");
        }

        const data = response.status === 204 ? null : await response.json().catch(() => null);
        if (!response.ok) {
            const error = new Error(data?.error ?? "Something went wrong. Please try again.");
            error.status = response.status;
            error.data = data;
            throw error;
        }
        return data;
    };

    // get("List") -> OnGetListAsync, post("Create", obj) -> OnPostCreateAsync (JSON body),
    // postForm("Create", formData) -> OnPostCreateAsync (multipart, used for file uploads).
    const get = (handler) => request(handler);
    const post = (handler, json = {}) => request(handler, { method: "POST", json });
    const postForm = (handler, formData) => request(handler, { method: "POST", formData });

    // Disable a button while its request is running, so it can't be clicked twice.
    const setBusy = (button, busy) => {
        button.disabled = busy;
        button.setAttribute("aria-busy", String(busy));
    };

    return {
        escapeHtml, formatCurrency, formatDate, formatTime, currencyCode, statusBadge, tableMessageRow,
        showToast, showError, get, post, postForm, setBusy
    };
})();
