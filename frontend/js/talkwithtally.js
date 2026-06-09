// ═══════════════════════════════════════════════════════════════
// TalkWithTally — Frontend Logic
// Depends on: jQuery, Bootstrap 5
// ═══════════════════════════════════════════════════════════════

const TWT = (function ($) {
  "use strict";

  // ── Config ─────────────────────────────────────────────────
  const API_BASE = "http://127.0.0.1:8081/api/tally"; // adjust to match your routing
  const STORAGE_KEY = "twt_setup_done"; // localStorage key
  const SYNC_POLL_MS = 5000; // poll sync status every 5s after sync-now
  const MAX_POLL_TRIES = 24; // give up polling after ~2 min

  // ── Internal state ─────────────────────────────────────────
  let _isOpen = false;
  let _setupDone = false;
  let _syncPollTimer = null;
  let _pollCount = 0;
  let _isSending = false;
  let _downloadUrl = "#";
  let _syncBaselineAt = null;
  let _lastSyncStatus = null;

  // ── DOM refs (populated in init) ───────────────────────────
  let $widget,
    $trigger,
    $chatArea,
    $input,
    $chips,
    $syncText,
    $syncIcon,
    $syncBtn,
    $syncToast,
    $statusDot,
    $screenSplash,
    $screenNotConnected,
    $screenChat;

  // ── Safe Helpers for Global Variables ──────────────────────
  function getCompanyId() {
    if (typeof TWT_COMPANY_ID !== "undefined" && TWT_COMPANY_ID !== null) {
      return TWT_COMPANY_ID;
    }
    // Fallback: try reading from a data-attribute on the widget wrapper
    if ($widget && $widget.data("company-id")) {
      return $widget.data("company-id");
    }
    console.warn(
      "TWT: TWT_COMPANY_ID is not defined globally. Defaulting to 5.",
    );
    return 5;
  }

  // ── API helper (sends dev sync token for local testing) ────
  function apiAjax(options) {
    let headers = $.extend({}, options.headers || {});
    if (typeof TWT_SYNC_TOKEN !== "undefined" && TWT_SYNC_TOKEN) {
      headers["X-Sync-Token"] = TWT_SYNC_TOKEN;
    }
    return $.ajax(
      $.extend({}, options, {
        headers: headers,
      }),
    );
  }

  // ── Init ───────────────────────────────────────────────────
  function init() {
    console.log("TWT: Initializing widget logic...");

    // Cache DOM refs
    $widget = $("#twt-widget");
    $trigger = $("#twt-trigger-btn");
    $chatArea = $("#twt-chat-area");
    $input = $("#twt-input");
    $chips = $("#twt-chips");
    $syncText = $("#twt-sync-text");
    $syncIcon = $(".twt-sync-icon");
    $syncBtn = $("#twt-sync-now-btn");
    $syncToast = $("#twt-sync-toast");
    $statusDot = $("#twt-status-dot");
    $screenSplash = $("#twt-screen-splash");
    $screenNotConnected = $("#twt-screen-notconnected");
    $screenChat = $("#twt-screen-chat");

    if (!$widget.length || !$trigger.length) {
      console.error(
        "TWT: Critical DOM elements (#twt-widget or #twt-trigger-btn) not found.",
      );
    }

    // Check if first-time setup was already done for this browser session
    _setupDone = localStorage.getItem(STORAGE_KEY) === "1";
    console.log("TWT: Setup status from localStorage:", _setupDone);

    // Bind events
    $trigger.off("click").on("click", toggleWidget);
    $("#twt-close-btn").off("click").on("click", closeWidget);
    $("#twt-splash-proceed").off("click").on("click", onSplashProceed);
    $("#twt-nc-retry").off("click").on("click", checkConnectionAndProceed);
    $syncBtn.off("click").on("click", onSyncNow);
    $("#twt-send-btn").off("click").on("click", onSend);

    $input.off("keydown").on("keydown", function (e) {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        onSend();
      }
    });

    // Download button gets URL from sync-status response
    $("#twt-download-btn")
      .off("click")
      .on("click", function () {
        window.open(_downloadUrl, "_blank");
      });

    $("#twt-copy-key-btn")
      .off("click")
      .on("click", function () {
        let key = $(this).data("key");
        if (!key) return;
        navigator.clipboard.writeText(key).then(function () {
          $("#twt-copy-key-btn").text("✓ Copied!");
          setTimeout(function () {
            $("#twt-copy-key-btn").text("📋 Copy Key");
          }, 2000);
        });
      });
  }

  // ── Widget toggle ──────────────────────────────────────────
  function toggleWidget() {
    console.log("TWT: Trigger clicked. Current state (isOpen):", _isOpen);
    if (_isOpen) {
      closeWidget();
    } else {
      openWidget();
    }
  }

  function openWidget() {
    if (window.innerWidth < 480) {
      alert(
        "TalkWithTally is optimised for desktop. Please use a wider screen.",
      );
      return;
    }

    _isOpen = true;
    $widget.removeClass("twt-hidden");

    if (!_setupDone) {
      console.log("TWT: First-time setup required. Showing splash screen.");
      showScreen("splash");
    } else {
      console.log("TWT: Setup already completed. Initiating connection check.");
      showScreen("chat");
      checkConnectionAndProceed();
    }
  }

  function closeWidget() {
    _isOpen = false;
    $widget.addClass("twt-hidden");
    clearInterval(_syncPollTimer);
  }

  function showScreen(name) {
    $screenSplash.addClass("twt-hidden");
    $screenNotConnected.addClass("twt-hidden");
    $screenChat.addClass("twt-hidden");

    if (name === "splash") $screenSplash.removeClass("twt-hidden");
    if (name === "not-connected") $screenNotConnected.removeClass("twt-hidden");
    if (name === "chat") $screenChat.removeClass("twt-hidden");
  }

  // Splash "Get Started" clicked
  function onSplashProceed() {
    console.log("TWT: Splash proceed clicked.");
    showScreen("not-connected"); // immediately show loading state...
    checkConnectionAndProceed(); // ...then check if connector is installed
  }

  // ── Connection check ───────────────────────────────────────
  function checkConnectionAndProceed() {
    console.log("TWT: Requesting sync-status from API...");
    setSyncText("Checking Tally connection...");
    setStatusDot(""); // neutral

    apiAjax({
      url: API_BASE + "/sync-status",
      method: "GET",
      success: function (res) {
        console.log("TWT: Sync status response received:", res);
        if (res.status === "connected") {
          _setupDone = true;
          localStorage.setItem(STORAGE_KEY, "1");
          showScreen("chat");
          _lastSyncStatus = res;
          updateSyncBar(res);
          loadSuggestions();
          loadHistory(res);
        } else {
          console.log("TWT: Connector is not connected or no data exists yet.");
          _downloadUrl = res.downloadUrl || "#";
          showScreen("not-connected");
          loadActivationKey();
        }
      },
      error: function (xhr, status, error) {
        console.error("TWT: Connection check failed.", status, error);
        setSyncText("Connection error");
        setStatusDot("error");
      },
    });
  }

  // ── Activation Key ─────────────────────────────────────────
  function loadActivationKey() {
    $("#twt-activation-key-display").text("Loading...");
    apiAjax({
      url: API_BASE + "/my-activation-key",
      method: "GET",
      success: function (res) {
        let key = res.activationKey || "";
        let formatted = key.match(/.{1,4}/g)?.join("-") || key;
        $("#twt-activation-key-display").text(formatted);
        $("#twt-copy-key-btn").data("key", key);
      },
      error: function () {
        $("#twt-activation-key-display").text("Unable to load key");
      },
    });
  }

  // ── Sync bar ───────────────────────────────────────────────
  function updateSyncBar(res) {
    if (!res.lastSyncedAt) {
      setSyncText("Never synced");
      setStatusDot("not-synced");
      return;
    }

    let syncDate = new Date(res.lastSyncedAt);
    let now = new Date();
    let diffMin = Math.floor((now - syncDate) / 60000);
    let label = "";

    if (diffMin < 1) label = "Just now";
    else if (diffMin < 60) label = diffMin + " min ago";
    else {
      let isToday = syncDate.toDateString() === now.toDateString();
      let timeStr = syncDate.toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
      });
      label = isToday
        ? "Today, " + timeStr
        : syncDate.toLocaleDateString([], {
            month: "short",
            day: "numeric",
          }) +
          ", " +
          timeStr;
    }

    if (diffMin > 30) {
      setStatusDot("not-synced"); // amber dot
      setSyncText("⚠ " + label + " — Tally may be offline");
    } else {
      setStatusDot(
        (res.syncStatus || "").toLowerCase() === "success"
          ? "connected"
          : "error",
      );
      setSyncText(label);
    }
  }

  function setSyncText(text) {
    $syncText.text(text);
  }

  function setStatusDot(state) {
    $statusDot.removeClass("connected not-synced error");
    if (state) $statusDot.addClass(state);
  }

  function parseSyncDate(value) {
    if (!value) return null;
    let d = new Date(String(value).replace(" ", "T"));
    return isNaN(d.getTime()) ? null : d;
  }

  function stopSyncPolling() {
    if (_syncPollTimer) {
      clearInterval(_syncPollTimer);
      _syncPollTimer = null;
    }
  }

  function finishSyncPolling(res, message) {
    stopSyncPolling();
    $syncIcon.removeClass("spinning");
    $syncBtn.prop("disabled", false);
    if (res) {
      _lastSyncStatus = res;
      updateSyncBar(res);
      showSyncToast();
    } else if (message) {
      setSyncText(message);
      setStatusDot("not-synced");
    }
  }

  // ── Sync Now button ────────────────────────────────────────
  function onSyncNow() {
    stopSyncPolling();
    $syncBtn.prop("disabled", true);
    $syncIcon.addClass("spinning");
    setSyncText("Syncing...");
    $syncToast.addClass("twt-hidden");

    apiAjax({
      url: API_BASE + "/sync-status",
      method: "GET",
      success: function (res) {
        _lastSyncStatus = res;
        _syncBaselineAt = parseSyncDate(res.lastSyncedAt) || new Date(0);
        queueSyncNow();
      },
      error: function () {
        _syncBaselineAt = new Date(0);
        queueSyncNow();
      },
    });
  }

  function queueSyncNow() {
    apiAjax({
      url: API_BASE + "/sync-now",
      method: "POST",
      success: function () {
        _pollCount = 0;
        pollForSyncComplete();
        _syncPollTimer = setInterval(pollForSyncComplete, SYNC_POLL_MS);
      },
      error: function () {
        finishSyncPolling(_lastSyncStatus, "Sync request failed");
      },
    });
  }

  function pollForSyncComplete() {
    _pollCount++;
    if (_pollCount > MAX_POLL_TRIES) {
      finishSyncPolling(
        _lastSyncStatus,
        "Sync timed out — is the Tally connector running?",
      );
      return;
    }

    apiAjax({
      url: API_BASE + "/sync-status",
      method: "GET",
      success: function (res) {
        if (res.status !== "connected") return;

        let syncDate = parseSyncDate(res.lastSyncedAt);
        if (!syncDate) return;

        let syncStatus = (res.syncStatus || "").toLowerCase();
        let hasNewerSync = syncDate > _syncBaselineAt;
        let syncSucceeded = syncStatus === "success";

        if (hasNewerSync && syncSucceeded) {
          finishSyncPolling(res);
        }
      },
    });
  }

  function showSyncToast() {
    $syncToast.removeClass("twt-hidden");
    setTimeout(function () {
      $syncToast.addClass("twt-hidden");
    }, 3500);
  }

  // ── Suggestions ────────────────────────────────────────────
  function loadSuggestions() {
    apiAjax({
      url: API_BASE + "/suggestions",
      method: "GET",
      success: function (suggestions) {
        $chips.empty();
        $.each(suggestions, function (_, s) {
          let $chip = $('<button class="twt-chip">')
            .text(s.Text)
            .on("click", function () {
              $input.val(s.Text);
              onSend();
              $chips.addClass("twt-hidden");
            });
          $chips.append($chip);
        });
      },
    });
  }

  // ── Chat ───────────────────────────────────────────────────
  function onSend() {
    let message = $input.val().trim();
    if (!message || _isSending) return;

    _isSending = true;
    $input.val("");
    $("#twt-send-btn").prop("disabled", true);
    $chips.addClass("twt-hidden");

    appendMessage("user", message);
    let $typing = appendTypingIndicator();

    apiAjax({
      url: API_BASE + "/chat",
      method: "POST",
      contentType: "application/json",
      data: JSON.stringify({
        companyId: getCompanyId(),
        message: message,
      }),
      success: function (res) {
        $typing.remove();
        renderAIResponse(res.response);
      },
      error: function (xhr) {
        $typing.remove();
        let errMsg = "🤖 Something went wrong. Please try again.";
        if (xhr.status === 429)
          errMsg = "⏳ Too many requests. Please wait a moment.";
        appendMessage("ai", errMsg);
      },
      complete: function () {
        _isSending = false;
        $("#twt-send-btn").prop("disabled", false);
        $input.focus();
        scrollToBottom();
      },
    });
  }

  function renderAIResponse(text, dateStr) {
    if (!text) {
      appendMessage("ai", "No response received.", dateStr);
      scrollToBottom();
      return;
    }

    if (looksLikeTable(text)) {
      let $bubble = buildTableBubble(text);
      appendRawBubble($bubble, dateStr);
    } else {
      appendMessage("ai", text, dateStr);
    }
    scrollToBottom();
  }

  function looksLikeTable(text) {
    let lines = text.split("\n").filter((l) => l.trim().length > 0);

    // Ignore if lines are formatted as standard markdown lists
    let hasListMarkers = lines.some((l) => /^\s*(\d+\.|[\*\-\+])\s+/.test(l));
    if (hasListMarkers) return false;

    // Check for standard colon separation key: value
    let keyValueLines = lines.filter(
      (l) => l.includes(":") && l.split(":").length === 2,
    );

    if (keyValueLines.length >= 3) {
      // Ensure keys are short label metrics (under 25 chars), not full phrases
      let allKeysShort = keyValueLines.every(
        (l) => l.split(":")[0].trim().length < 25,
      );
      return allKeysShort;
    }

    return false;
  }

  function buildTableBubble(text) {
    let lines = text.split("\n").filter((l) => l.trim() && l.includes(":"));
    let $table = $('<table class="twt-response-table">');
    let $tbody = $("<tbody>");

    $.each(lines, function (_, line) {
      let parts = line.split(":");
      let key = parts[0].trim();
      let val = parts.slice(1).join(":").trim();
      $tbody.append($("<tr>").append($("<td>").text(key), $("<td>").text(val)));
    });

    $table.append($tbody);
    let $bubble = $('<div class="twt-bubble">').append($table);
    return $bubble;
  }

  // ── DOM helpers ────────────────────────────────────────────
  function appendMessage(role, text, dateStr) {
    let isUser = role === "user";
    let avatar = isUser ? "👤" : "🏦";
    let dateObj = dateStr
      ? new Date(String(dateStr).replace(" ", "T"))
      : new Date();
    let timeStr = isNaN(dateObj.getTime())
      ? new Date().toLocaleTimeString([], {
          hour: "2-digit",
          minute: "2-digit",
        })
      : dateObj.toLocaleTimeString([], {
          hour: "2-digit",
          minute: "2-digit",
        });

    let htmlText;
    if (isUser) {
      htmlText = $("<span>").text(text).html(); // Escape user inputs safely
    } else {
      htmlText = text
        .replace(/\*\*(.*?)\*\*/g, "<strong>$1</strong>") // Formats **bold**
        .replace(/\*(.*?)\*/g, "<em>$1</em>") // Formats *italics*
        .replace(/\n/g, "<br>"); // Maintain linebreaks
    }

    let $row = $('<div class="twt-msg-row ' + (isUser ? "user" : "") + '">');
    let $avatar = $('<div class="twt-msg-avatar">').text(avatar);
    let $col = $("<div>").css({
      display: "flex",
      flexDirection: "column",
      maxWidth: "80%",
    });
    let $bubble = $('<div class="twt-bubble">').html(htmlText);
    let $time = $('<div class="twt-msg-time">').text(timeStr);

    $col.append($bubble, $time);
    $row.append($avatar, $col);
    $chatArea.append($row);
    scrollToBottom();
    return $row;
  }

  function appendRawBubble($bubble, dateStr) {
    let dateObj = dateStr
      ? new Date(String(dateStr).replace(" ", "T"))
      : new Date();
    let timeStr = isNaN(dateObj.getTime())
      ? new Date().toLocaleTimeString([], {
          hour: "2-digit",
          minute: "2-digit",
        })
      : dateObj.toLocaleTimeString([], {
          hour: "2-digit",
          minute: "2-digit",
        });
    let $row = $('<div class="twt-msg-row">');
    let $avatar = $('<div class="twt-msg-avatar">').text("🏦");
    let $col = $("<div>").css({
      display: "flex",
      flexDirection: "column",
      maxWidth: "88%",
    });
    let $time = $('<div class="twt-msg-time">').text(timeStr);
    $col.append($bubble, $time);
    $row.append($avatar, $col);
    $chatArea.append($row);
    scrollToBottom();
  }

  function appendTypingIndicator() {
    let $row = $('<div class="twt-msg-row">');
    let $avatar = $('<div class="twt-msg-avatar">').text("🏦");
    let $typing = $('<div class="twt-typing">').append(
      "<span></span><span></span><span></span>",
    );
    $row.append($avatar, $typing);
    $chatArea.append($row);
    scrollToBottom();
    return $row;
  }

  function showWelcomeMessage(res) {
    $chatArea.empty();
    let ledgers = res.totalLedgers ? res.totalLedgers.toLocaleString() : "—";
    let vouchers = res.totalVouchers ? res.totalVouchers.toLocaleString() : "—";
    let msg =
      `👋 Welcome to TalkWithTally!\n\n` +
      `📊 ${ledgers} ledgers and ${vouchers} vouchers are synced from your Tally data.\n\n` +
      `Ask me anything — ledger balances, sales summaries, debtor lists, or try a suggestion below.`;
    appendMessage("ai", msg);
  }

  // ── Chat History ───────────────────────────────────────────
  function loadHistory(syncRes) {
    let companyId = getCompanyId();
    console.log("TWT: Loading history for companyId:", companyId);
    $chatArea.empty();

    apiAjax({
      url: API_BASE + "/history/" + companyId + "?page=1&pageSize=20",
      method: "GET",
      success: function (logs) {
        console.log("TWT: History logs received:", logs);
        if (!logs || logs.length === 0) {
          showWelcomeMessage(syncRes);
          return;
        }

        let ordered = logs.slice().reverse();
        $.each(ordered, function (_, log) {
          appendMessage("user", log.UserQuestion, log.CreatedDate);
          if (log.AIResponse && log.IsSuccess) {
            renderAIResponse(log.AIResponse, log.CreatedDate);
          }
        });

        $chatArea.append(
          $('<div class="twt-history-divider">').text(
            "↑ Previous conversations",
          ),
        );
      },
      error: function (xhr, status, error) {
        console.error("TWT: History API call failed:", status, error);
        showWelcomeMessage(syncRes);
      },
    });
  }

  function scrollToBottom() {
    let el = $chatArea[0];
    if (el) el.scrollTop = el.scrollHeight;
  }

  // ── Public API ─────────────────────────────────────────────
  return {
    init: init,
  };
})(jQuery);

// Boot on DOM ready
$(document).ready(function () {
  TWT.init();
});
