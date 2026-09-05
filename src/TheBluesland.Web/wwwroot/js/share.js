(() => {
    "use strict";
    document.querySelectorAll(".playlist-share").forEach((panel) => {
        const data = { title: panel.dataset.shareTitle, url: panel.dataset.shareUrl };
        const nativeButton = panel.querySelector("[data-native-share]");
        const copyButton = panel.querySelector("[data-copy-share]");
        const status = panel.querySelector("[data-share-status]");
        const input = panel.querySelector("input");
        const selectLink = () => {
            input.focus();
            input.select();
            status.textContent = "Copy the selected link to share this playlist.";
        };
        copyButton.hidden = false;
        copyButton.addEventListener("click", async () => {
            try {
                await navigator.clipboard.writeText(data.url);
                status.textContent = "Link copied.";
            } catch {
                selectLink();
            }
        });
        if (typeof navigator.share === "function") {
            nativeButton.hidden = false;
            nativeButton.addEventListener("click", async () => {
                status.textContent = "";
                nativeButton.disabled = true;
                try {
                    await navigator.share(data);
                } catch (error) {
                    if (error.name !== "AbortError") {
                        status.textContent = "Device sharing is unavailable. Use Twitter / X, WhatsApp, or copy the link.";
                    }
                } finally {
                    nativeButton.disabled = false;
                }
            });
        }
    });
})();
