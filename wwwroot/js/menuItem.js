(() => {
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("a[data-tab-navigation]").forEach((tabLink) => {
            tabLink.addEventListener("click", (event) => {
                if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
                    return;
                }

                event.preventDefault();
                window.location.replace(tabLink.href);
            });
        });

        const addToCartForm = document.querySelector("#add-to-cart-form");
        if (addToCartForm) {
            const optionAlert = addToCartForm.querySelector("#option-validation-alert");
            const hasMissingOption = () => {
                const optionInputs = [...addToCartForm.querySelectorAll('input[type="radio"][required]')];
                const optionGroups = new Set(optionInputs.map((input) => input.name));
                return [...optionGroups].some((name) =>
                    !optionInputs.some((input) => input.name === name && input.checked));
            };

            addToCartForm.addEventListener("submit", (event) => {
                if (hasMissingOption()) {
                    event.preventDefault();
                    optionAlert?.classList.remove("d-none");
                    optionAlert?.scrollIntoView({ behavior: "smooth", block: "center" });
                    return;
                }

                optionAlert?.classList.add("d-none");
            });

            addToCartForm.addEventListener("change", (event) => {
                if (event.target.matches('input[type="radio"]') && !hasMissingOption()) {
                    optionAlert?.classList.add("d-none");
                }
            });
        }

        const quantityInput = document.querySelector("#quantity-input");
        const quantityDisplay = document.querySelector("#quantity-display");
        const quantityButtons = document.querySelectorAll("[data-quantity-change]");

        const updateQuantityDisplay = (value) => {
            const min = 1;
            const max = 20;
            const nextValue = Math.min(Math.max(value, min), max);

            if (quantityInput) {
                quantityInput.value = String(nextValue);
            }

            if (quantityDisplay) {
                quantityDisplay.textContent = String(nextValue);
            }
        };

        if (quantityInput && quantityDisplay) {
            const initialValue = Number.parseInt(quantityInput.value, 10) || 1;
            updateQuantityDisplay(initialValue);
        }

        quantityButtons.forEach((button) => {
            button.addEventListener("click", () => {
                if (!quantityInput) {
                    return;
                }

                const delta = Number.parseInt(button.getAttribute("data-quantity-change") ?? "0", 10);
                const currentValue = Number.parseInt(quantityInput.value, 10) || 1;
                updateQuantityDisplay(currentValue + delta);
            });
        });

        const remarkInput = document.querySelector(".remark-input");
        const characterCounter = document.querySelector(".character-counter");

        if (remarkInput && characterCounter) {
            const updateCharacterCounter = () => {
                characterCounter.textContent = `${remarkInput.value.length}/${remarkInput.maxLength}`;
            };

            remarkInput.addEventListener("input", updateCharacterCounter);
            updateCharacterCounter();
        }
    });
})();
