(() => {
    const STORAGE_KEY = "admin-theme";

    const getSavedTheme = () => {
        try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
    };

    const preferredTheme = () =>
        getSavedTheme() ?? (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");

    const applyTheme = (theme) => {
        document.documentElement.setAttribute("data-bs-theme", theme);

        const toggle = document.getElementById("theme-toggle");
        if (toggle) {
            const isDark = theme === "dark";
            toggle.innerHTML = `<i class="fa-solid ${isDark ? "fa-sun" : "fa-moon"}"></i>`;
            toggle.setAttribute("aria-label", isDark ? "Switch to light mode" : "Switch to dark mode");
            toggle.title = isDark ? "Light mode" : "Dark mode";
        }
        document.dispatchEvent(new CustomEvent("admin-theme-change", { detail: { theme } }));
    };

    document.documentElement.setAttribute("data-bs-theme", preferredTheme());

    document.addEventListener("DOMContentLoaded", () => {
        applyTheme(preferredTheme());

        document.getElementById("theme-toggle")?.addEventListener("click", () => {
            const next = document.documentElement.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
            try { localStorage.setItem(STORAGE_KEY, next); } catch { }
            applyTheme(next);
        });
    });
})();
