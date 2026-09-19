// Live username availability check for the registration form.
// Uses event delegation so it keeps working across Blazor enhanced navigation.
(function () {
    const FORMAT = /^[A-Za-z0-9_]{3,30}$/;
    const DEBOUNCE_MS = 400;

    let timer = null;
    let controller = null;
    let sequence = 0;
    const cache = new Map();

    function show(input, state, message) {
        const feedback = document.getElementById("username-feedback");
        if (!feedback) return;

        input.classList.remove("is-valid", "is-invalid");
        feedback.classList.remove("text-success", "text-danger", "text-muted");

        if (state === "ok") {
            input.classList.add("is-valid");
            feedback.classList.add("text-success");
        } else if (state === "bad") {
            input.classList.add("is-invalid");
            feedback.classList.add("text-danger");
        } else {
            feedback.classList.add("text-muted");
        }

        feedback.textContent = message;
    }

    async function check(input) {
        const value = input.value.trim();
        const mine = ++sequence;

        if (controller) controller.abort();

        if (value.length === 0) {
            show(input, "idle", "3-30 characters: letters, numbers and underscores.");
            return;
        }

        if (!FORMAT.test(value)) {
            show(input, "bad", "Use 3-30 letters, numbers or underscores.");
            return;
        }

        const key = value.toLowerCase();
        if (cache.has(key)) {
            render(input, value, cache.get(key));
            return;
        }

        show(input, "idle", "Checking availability...");
        controller = new AbortController();

        try {
            const response = await fetch(
                "/auth/username-available?username=" + encodeURIComponent(value),
                { signal: controller.signal, headers: { Accept: "application/json" } });

            if (mine !== sequence) return; // a newer keystroke superseded this one

            if (response.status === 429) {
                show(input, "idle", "Too many checks - slow down for a moment.");
                return;
            }
            if (!response.ok) {
                show(input, "idle", "Could not check right now. We will verify when you submit.");
                return;
            }

            const result = await response.json();
            cache.set(key, result);
            render(input, value, result);
        } catch (error) {
            if (error.name !== "AbortError" && mine === sequence) {
                show(input, "idle", "Could not check right now. We will verify when you submit.");
            }
        }
    }

    function render(input, value, result) {
        if (result.available) {
            show(input, "ok", value + " is available.");
        } else if (result.reason === "invalid") {
            show(input, "bad", "Use 3-30 letters, numbers or underscores.");
        } else {
            show(input, "bad", value + " is already taken.");
        }
    }

    document.addEventListener("input", function (event) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement) || !input.matches("[data-username-check]")) return;

        clearTimeout(timer);
        timer = setTimeout(function () { check(input); }, DEBOUNCE_MS);
    });
})();
