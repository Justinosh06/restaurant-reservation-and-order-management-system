(() => {
    const form = document.getElementById("profile-form");
    const formError = document.getElementById("profile-form-error");
    const submitBtn = document.getElementById("profile-submit");
    const nameInput = document.getElementById("profile-name");
    const emailInput = document.getElementById("profile-email");

    // What is saved right now, so "Discard changes" can go back to it.
    let saved = { name: nameInput.value, email: emailInput.value };

    // The sidebar shows the same name and email, so it is updated after saving.
    const updateSidebar = (profile) => {
        const sidebar = document.querySelector(".sidebar .profile-btn");
        if (!sidebar) return;
        sidebar.querySelector(".email").textContent = profile.email;
        sidebar.querySelector(".name").textContent = profile.displayName;
    };

    const updateSummary = (profile) => {
        document.getElementById("profile-summary-name").textContent = profile.name || "No name set";
        document.getElementById("profile-summary-email").textContent = profile.email;
    };

    form.addEventListener("reset", (e) => {
        e.preventDefault();
        nameInput.value = saved.name;
        emailInput.value = saved.email;
        formError.classList.add("d-none");
        form.classList.remove("was-validated");
    });

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        AdminUI.setBusy(submitBtn, true);
        try {
            const profile = await AdminUI.post("Save", { name: nameInput.value.trim(), email: emailInput.value.trim() });
            saved = { name: profile.name ?? "", email: profile.email };
            form.classList.remove("was-validated");
            nameInput.value = saved.name;
            emailInput.value = saved.email;
            updateSummary(profile);
            updateSidebar(profile);
            AdminUI.showToast("Profile updated");
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });
})();
