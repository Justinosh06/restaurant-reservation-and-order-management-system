(() => {
    const form = document.getElementById("profile-form");
    const formError = document.getElementById("profile-form-error");
    const submitBtn = document.getElementById("profile-submit");
    const nameInput = document.getElementById("profile-name");
    const emailInput = document.getElementById("profile-email");
    const fileInput = document.getElementById("profile-image");
    const dropEl = document.getElementById("profile-image-drop");
    const imageError = document.getElementById("profile-image-error");
    const removeBtn = document.getElementById("profile-image-remove");

    const gender = form.dataset.gender;
    const maxImageBytes = Number(fileInput.dataset.maxBytes);
    const allowedTypes = fileInput.accept.split(",").map(type => type.trim());

    // What is saved right now, so "Discard changes" can go back to it.
    let saved = { name: nameInput.value, email: emailInput.value, imageUrl: form.dataset.imageUrl || null };
    let selectedImage = null;
    let previewUrl = null;
    let removeSavedImage = false;

    // Fills an .avatar element with the uploaded picture, or the gender drawing when there is none.
    const paintAvatar = (el, imageUrl) => {
        el.classList.remove("male", "female", "photo");
        el.classList.add(imageUrl ? "photo" : gender);
        el.innerHTML = imageUrl
            ? `<img class="avatar-photo" src="${AdminUI.escapeHtml(imageUrl)}" alt="" />`
            : `<img class="avatar-img" src="/images/avatar-${gender}.png" alt="" />`;
    };

    const showImage = (imageUrl) => {
        paintAvatar(dropEl, imageUrl);
        removeBtn.classList.toggle("d-none", !imageUrl);
    };

    const clearSelection = () => {
        if (previewUrl) URL.revokeObjectURL(previewUrl);
        selectedImage = null;
        previewUrl = null;
        fileInput.value = "";
    };

    const loadImage = (file) => {
        imageError.textContent = "";
        if (!file) return;
        if (!allowedTypes.includes(file.type)) {
            imageError.textContent = "Please choose a JPG, PNG or WebP image.";
            return;
        }
        if (file.size > maxImageBytes) {
            imageError.textContent = `Image must be ${maxImageBytes / (1024 * 1024)} MB or smaller.`;
            return;
        }

        clearSelection();
        selectedImage = file;
        previewUrl = URL.createObjectURL(file);
        removeSavedImage = false;
        showImage(previewUrl);
    };

    // The sidebar shows the same picture, name and email, so it is updated after saving.
    const updateSidebar = (profile) => {
        const sidebar = document.querySelector(".sidebar .profile-btn");
        if (!sidebar) return;
        paintAvatar(sidebar.querySelector(".avatar"), profile.imageUrl);
        sidebar.querySelector(".email").textContent = profile.email;
        sidebar.querySelector(".name").textContent = profile.displayName;
    };

    const updateSummary = (profile) => {
        paintAvatar(document.getElementById("profile-summary-avatar"), profile.imageUrl);
        document.getElementById("profile-summary-name").textContent = profile.name || "No name set";
        document.getElementById("profile-summary-email").textContent = profile.email;
    };

    dropEl.addEventListener("click", () => fileInput.click());
    document.getElementById("profile-image-choose").addEventListener("click", () => fileInput.click());
    fileInput.addEventListener("change", () => loadImage(fileInput.files[0]));

    removeBtn.addEventListener("click", () => {
        clearSelection();
        imageError.textContent = "";
        removeSavedImage = true;
        showImage(null);
    });

    dropEl.addEventListener("dragover", (e) => { e.preventDefault(); dropEl.classList.add("border-dark"); });
    dropEl.addEventListener("dragleave", () => dropEl.classList.remove("border-dark"));
    dropEl.addEventListener("drop", (e) => {
        e.preventDefault();
        dropEl.classList.remove("border-dark");
        loadImage(e.dataTransfer.files[0]);
    });

    form.addEventListener("reset", (e) => {
        e.preventDefault();
        nameInput.value = saved.name;
        emailInput.value = saved.email;
        clearSelection();
        removeSavedImage = false;
        imageError.textContent = "";
        formError.classList.add("d-none");
        form.classList.remove("was-validated");
        showImage(saved.imageUrl);
    });

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        const data = new FormData();
        data.append("name", nameInput.value.trim());
        data.append("email", emailInput.value.trim());
        data.append("removeImage", String(removeSavedImage));
        if (selectedImage) data.append("image", selectedImage);

        AdminUI.setBusy(submitBtn, true);
        try {
            const profile = await AdminUI.postForm("Save", data);
            saved = { name: profile.name ?? "", email: profile.email, imageUrl: profile.imageUrl };
            clearSelection();
            removeSavedImage = false;
            form.classList.remove("was-validated");
            nameInput.value = saved.name;
            emailInput.value = saved.email;
            showImage(saved.imageUrl);
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
