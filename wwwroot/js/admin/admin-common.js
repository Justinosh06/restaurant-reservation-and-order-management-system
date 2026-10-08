// Shared helpers for admin pages (frontend-only mock UI; no backend calls yet).
const AdminUI = (() => {
    const escapeHtml = (value) => String(value ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");

    const formatCurrency = (amountInCents) =>
        "RM " + (amountInCents / 100).toLocaleString("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    const formatDate = (date) =>
        new Date(date).toLocaleDateString("en-MY", { day: "numeric", month: "short", year: "numeric" });

    const formatTime = (date) =>
        new Date(date).toLocaleTimeString("en-MY", { hour: "2-digit", minute: "2-digit" });

    const generateId = (prefix) => prefix + "-" + Math.random().toString(36).slice(2, 8).toUpperCase();

    let toastTimer;
    const showToast = (message) => {
        let toast = document.getElementById("admin-toast");
        if (!toast) {
            toast = document.createElement("div");
            toast.id = "admin-toast";
            toast.className = "admin-toast";
            toast.setAttribute("role", "status");
            document.body.appendChild(toast);
        }
        toast.textContent = message;
        toast.classList.add("show");
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toast.classList.remove("show"), 2400);
    };

    return { escapeHtml, formatCurrency, formatDate, formatTime, generateId, showToast };
})();
