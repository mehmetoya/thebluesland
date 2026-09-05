// US-019: progressive enhancement over the server-rendered "Show more playlists" link
// (HomePage.razor). That link is the real, zero-JS mechanism - a plain GET to a URL that
// cumulatively reveals the next PageSize playlists (see PlaylistCataloguePage). This script only
// automates clicking it: when the link scrolls near the viewport, fetch its href, splice in the
// playlists the current page doesn't have yet, and repeat for whatever "load more" link (if any)
// the fetched page carries. Any failure (fetch error, missing IntersectionObserver, blocked CSP)
// leaves the real link in place and clickable - never a broken or half-updated page.
(function () {
  "use strict";

  if (!("IntersectionObserver" in window) || !("fetch" in window)) {
    return;
  }

  function enhance(trigger) {
    if (!trigger) {
      return;
    }

    var observer = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            observer.disconnect();
            loadNext(trigger);
          }
        });
      },
      { rootMargin: "400px 0px" },
    );
    observer.observe(trigger);
  }

  function loadNext(trigger) {
    var catalogue = trigger.closest(".catalogue");
    if (!catalogue) {
      return;
    }

    var alreadyShown = catalogue.querySelectorAll(".playlist-card-link").length;
    var originalLabel = trigger.textContent;
    trigger.textContent = "Loading…";
    trigger.setAttribute("aria-disabled", "true");

    fetch(trigger.href)
      .then(function (response) {
        if (!response.ok) {
          throw new Error("Unexpected status " + response.status);
        }
        return response.text();
      })
      .then(function (html) {
        var fetchedCatalogue = new DOMParser().parseFromString(html, "text/html").querySelector(".catalogue");
        if (!fetchedCatalogue) {
          throw new Error("No .catalogue in fetched page");
        }

        var fetchedCards = fetchedCatalogue.querySelectorAll(".playlist-card-link");
        var fragment = document.createDocumentFragment();
        for (var i = alreadyShown; i < fetchedCards.length; i++) {
          fragment.appendChild(fetchedCards[i].cloneNode(true));
        }

        var nextTrigger = fetchedCatalogue.querySelector(".load-more");
        trigger.remove();
        catalogue.appendChild(fragment);

        if (nextTrigger) {
          var clonedTrigger = nextTrigger.cloneNode(true);
          catalogue.appendChild(clonedTrigger);
          enhance(clonedTrigger);
        }

        history.replaceState(null, "", trigger.href);
      })
      .catch(function () {
        // Leave the (still-attached, unmodified) trigger clickable as a manual fallback.
        trigger.textContent = originalLabel;
        trigger.removeAttribute("aria-disabled");
        enhance(trigger);
      });
  }

  document.addEventListener("DOMContentLoaded", function () {
    enhance(document.querySelector(".load-more"));
  });
})();
