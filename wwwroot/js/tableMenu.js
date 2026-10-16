(() => {
    document.addEventListener("DOMContentLoaded", () => {
        const pills = document.querySelectorAll(".filter-pill");
        const items = document.querySelectorAll(".item-card-link");
        const menuTotalCount = document.getElementById("menu-total-count");
        const search = document.querySelector(".menu-search");
        const searchButton = search?.querySelector(".searchbar-btn");
        const searchModal = search?.querySelector(".search-modal");
        const searchForm = search?.querySelector(".searchbar-popup");
        const searchInput = search?.querySelector(".searchbar-input");
        const closeSearchButton = search?.querySelector(".searchbar-close");
        const recommendations = search?.querySelector(".search-recommendations");
        const recentSearches = search?.querySelector(".search-recent-searches");
        const recentSearchList = recentSearches?.querySelector(".search-recommendation-list");
        const topRated = search?.querySelector(".search-top-rated");
        const topRatedList = topRated?.querySelector(".search-recommendation-list");
        const searchMatches = search?.querySelector(".search-matches");
        const searchMatchList = searchMatches?.querySelector(".search-recommendation-list");
        const noRecommendations = searchMatches?.querySelector(".search-recommendation-empty");
        const activeSearch = search?.querySelector(".active-search");
        const activeSearchQuery = search?.querySelector(".active-search-query");
        const clearSearchButton = search?.querySelector(".clear-search");
        const recentSearchesStorageKey = "restaurant-menu-recent-searches";
        const maxRecentSearches = 5;
        let selectedCategory = "all";
        let searchTerm = "";

        const readRecentSearches = () => {
            try {
                const storedSearches = localStorage.getItem(recentSearchesStorageKey);
                const parsedSearches = storedSearches ? JSON.parse(storedSearches) : [];
                return Array.isArray(parsedSearches)
                    ? parsedSearches.filter(value => typeof value === "string" && value.trim()).slice(0, maxRecentSearches)
                    : [];
            } catch (error) {
                console.warn("Unable to read recent menu searches from local storage.", error);
                return [];
            }
        };

        const saveRecentSearch = value => {
            const normalizedValue = value.trim();
            if (!normalizedValue) {
                return;
            }

            const recent = readRecentSearches().filter(item =>
                item.toLocaleLowerCase() !== normalizedValue.toLocaleLowerCase());
            recent.unshift(normalizedValue);

            try {
                localStorage.setItem(recentSearchesStorageKey, JSON.stringify(recent.slice(0, maxRecentSearches)));
            } catch (error) {
                console.warn("Unable to save recent menu searches to local storage.", error);
            }
        };

        const appendSuggestion = (list, text, onSelect) => {
            const listItem = document.createElement("li");
            const suggestion = document.createElement("button");

            suggestion.type = "button";
            suggestion.className = "search-recommendation";
            suggestion.textContent = text;
            suggestion.addEventListener("click", onSelect);
            list.append(listItem);
            listItem.append(suggestion);
        };

        const updateRecommendations = query => {
            if (!recommendations || !recentSearches || !recentSearchList ||
                !topRated || !topRatedList || !searchMatches || !searchMatchList ||
                !noRecommendations) {
                return;
            }

            recentSearchList.replaceChildren();
            topRatedList.replaceChildren();
            searchMatchList.replaceChildren();
            const normalizedQuery = query.trim().toLocaleLowerCase();
            if (!normalizedQuery) {
                const recent = readRecentSearches();
                recent.forEach(value => appendSuggestion(recentSearchList, value, () => {
                    if (searchInput) {
                        searchInput.value = value;
                    }
                    searchForm?.requestSubmit();
                }));
                recentSearches.hidden = recent.length === 0;

                const ratedItems = Array.from(items)
                    .filter(item => Number(item.getAttribute("data-top-rated-order")) >= 0)
                    .sort((left, right) =>
                        Number(left.getAttribute("data-top-rated-order")) -
                        Number(right.getAttribute("data-top-rated-order")));
                ratedItems.forEach(item => {
                    const name = item.getAttribute("data-search-name") ?? "";
                    appendSuggestion(topRatedList, name, () => {
                        if (searchInput) {
                            searchInput.value = name;
                        }
                        searchForm?.requestSubmit();
                    });
                });
                topRated.hidden = ratedItems.length === 0;
                searchMatches.hidden = true;
                recommendations.hidden = recent.length === 0 && ratedItems.length === 0;
                return;
            }

            recentSearches.hidden = true;
            topRated.hidden = true;
            searchMatches.hidden = false;
            recommendations.hidden = false;

            const matchingItems = Array.from(items).filter(item => {
                const name = item.getAttribute("data-search-name") ?? "";
                return name.toLocaleLowerCase().includes(normalizedQuery);
            });

            matchingItems.forEach(item => {
                const name = item.getAttribute("data-search-name") ?? "";
                appendSuggestion(searchMatchList, name, () => {
                    if (searchInput) {
                        searchInput.value = name;
                    }
                    searchForm?.requestSubmit();
                });
            });

            noRecommendations.hidden = matchingItems.length > 0;
        };

        const filterItems = () => {
            let visibleCount = 0;

            items.forEach(item => {
                const itemCategory = item.getAttribute("data-category");
                const itemName = item.getAttribute("data-search-name")?.toLocaleLowerCase() ?? "";
                const matchesCategory = selectedCategory === "all" || itemCategory === selectedCategory;
                const matchesSearch = !searchTerm || itemName.includes(searchTerm);
                const isVisible = matchesCategory && matchesSearch;

                item.style.display = isVisible ? "" : "none";
                if (isVisible) {
                    visibleCount++;
                }
            });

            if (menuTotalCount) {
                menuTotalCount.textContent = visibleCount;
            }
        };

        const closeSearchPopup = () => {
            if (!searchModal || !searchButton) {
                return;
            }

            searchModal.hidden = true;
            searchButton.setAttribute("aria-expanded", "false");
        };

        const openSearchPopup = () => {
            if (!searchModal || !searchButton || !searchInput) {
                return;
            }

            searchModal.hidden = false;
            searchButton.setAttribute("aria-expanded", "true");
            searchInput.value = searchTerm;
            updateRecommendations(searchInput.value);
            searchInput.focus();
        };

        pills.forEach(pill => {
            pill.addEventListener("click", () => {
                pills.forEach(p => p.classList.remove("active"));
                pill.classList.add("active");
                selectedCategory = pill.getAttribute("data-category") ?? "all";
                filterItems();
            });
        });

        searchButton?.addEventListener("click", openSearchPopup);

        searchInput?.addEventListener("input", () => {
            updateRecommendations(searchInput.value);
        });

        searchForm?.addEventListener("submit", event => {
            event.preventDefault();
            const submittedSearch = searchInput?.value.trim() ?? "";
            searchTerm = submittedSearch.toLocaleLowerCase();
            saveRecentSearch(submittedSearch);

            if (activeSearchQuery) {
                activeSearchQuery.textContent = searchInput?.value.trim() ?? "";
            }

            if (activeSearch) {
                activeSearch.hidden = !searchTerm;
            }

            if (searchButton) {
                searchButton.hidden = Boolean(searchTerm);
            }

            closeSearchPopup();
            filterItems();
        });

        closeSearchButton?.addEventListener("click", closeSearchPopup);
        activeSearchQuery?.addEventListener("click", openSearchPopup);

        clearSearchButton?.addEventListener("click", () => {
            searchTerm = "";
            if (searchInput) {
                searchInput.value = "";
            }
            if (activeSearch) {
                activeSearch.hidden = true;
            }
            if (searchButton) {
                searchButton.hidden = false;
            }
            filterItems();
        });

        searchInput?.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeSearchPopup();
            }
        });

        searchModal?.addEventListener("click", event => {
            if (event.target === searchModal) {
                closeSearchPopup();
            }
        });
    });
})();
