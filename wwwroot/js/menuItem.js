(() => {
    document.addEventListener("DOMContentLoaded", () => {
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
