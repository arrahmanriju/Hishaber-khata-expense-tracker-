"use strict";

for (const ledger of document.querySelectorAll("[data-ledger]")) {
    const tools = ledger.querySelector(".ledger-tools");
    if (!tools) continue;
    const search = tools.querySelector("[data-search]");
    const category = tools.querySelector("[data-category]");
    const type = tools.querySelector("[data-type]");
    const status = tools.querySelector("[data-filter-count]");
    const entries = Array.from(ledger.querySelectorAll("[data-entry]"));
    const empty = ledger.querySelector("[data-filter-empty]");
    const table = ledger.querySelector(".ledger-table");
    tools.hidden = false;

    function filter() {
        const query = search.value.trim().toLocaleLowerCase();
        let matches = 0;
        for (const entry of entries) {
            const visible = (!category.value || category.value === entry.dataset.category)
                && (!type?.value || type.value === entry.dataset.type)
                && entry.dataset.text.toLocaleLowerCase().includes(query);
            entry.hidden = !visible;
            if (visible) matches++;
        }
        empty.hidden = matches > 0;
        table.hidden = matches === 0;
        status.textContent = matches === 1 ? "1 matching transaction" : `${matches} matching transactions`;
    }

    search.addEventListener("input", filter);
    category.addEventListener("change", filter);
    type?.addEventListener("change", filter);
    empty.querySelector("[data-clear]").addEventListener("click", () => {
        search.value = "";
        category.value = "";
        if (type) type.value = "";
        filter();
        search.focus();
    });
}

const monthJump = document.querySelector("[data-month-jump]");
if (monthJump) {
    monthJump.closest(".period").querySelector("[data-period-label]").hidden = true;
    monthJump.hidden = false;
    monthJump.addEventListener("change", () => {
        if (monthJump.value) window.location.assign(monthJump.value);
    });
}

for (const details of document.querySelectorAll(".quick-add, .mobile-more")) {
    details.addEventListener("keydown", event => {
        if (event.key === "Escape" && details.open) {
            details.open = false;
            details.querySelector("summary").focus();
        }
    });
    document.addEventListener("click", event => {
        if (details.open && !details.contains(event.target)) details.open = false;
    });
}

const deleteForms = document.querySelectorAll("[data-confirm-delete]");
if (deleteForms.length) {
    const dialog = document.createElement("dialog");
    dialog.className = "confirm-dialog";
    dialog.setAttribute("aria-labelledby", "confirm-title");
    dialog.setAttribute("aria-describedby", "confirm-message");
    dialog.innerHTML = `
        <h2 id="confirm-title">Delete transaction?</h2>
        <p id="confirm-message">This transaction will be removed from your ledger and monthly totals.</p>
        <form method="dialog" class="form-actions">
            <button type="submit" class="button button-secondary" value="cancel" autofocus>Keep transaction</button>
            <button type="submit" class="button button-danger" value="delete">Delete</button>
        </form>`;
    document.body.append(dialog);
    let pendingForm;
    let trigger;
    dialog.addEventListener("close", () => {
        if (dialog.returnValue === "delete" && pendingForm) {
            HTMLFormElement.prototype.submit.call(pendingForm);
        } else {
            trigger?.focus();
        }
        pendingForm = null;
        trigger = null;
    });
    for (const form of deleteForms) {
        form.querySelector("button").disabled = false;
        form.addEventListener("submit", event => {
            if (typeof dialog.showModal !== "function") {
                if (!window.confirm("Delete this transaction?")) event.preventDefault();
                return;
            }
            event.preventDefault();
            pendingForm = form;
            trigger = event.submitter;
            dialog.returnValue = "";
            dialog.showModal();
        });
    }
}

window.khataSpendingTrend = function renderSpendingTrend(months, symbol, currency) {
    const values = months.map(({ expense }) => Number(expense));
    const hasSpend = values.some(value => value !== 0);
    const details = document.querySelector("#trend-data");
    const empty = document.querySelector("#trend-empty");
    const chartSlot = document.querySelector("#spending-chart");
    empty.hidden = hasSpend;
    if (!hasSpend) return;

    const list = document.querySelector("#trend-values");
    const fullAmount = new Intl.NumberFormat(undefined, { style: "currency", currency });
    const axisAmount = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });
    for (const { month, expense } of months) {
        const group = document.createElement("div");
        const label = document.createElement("dt");
        const amount = document.createElement("dd");
        label.textContent = month;
        amount.textContent = fullAmount.format(expense);
        amount.className = "money";
        group.append(label, amount);
        list.append(group);
    }
    details.hidden = false;
    if (typeof window.Chart !== "function") {
        details.open = true;
        return;
    }

    chartSlot.hidden = false;
    const axisFont = { family: "Inter, system-ui, sans-serif", size: 12 };
    new window.Chart(document.querySelector("#expense-trend"), {
        type: "bar",
        data: {
            labels: months.map(({ month }) => month.split(" ")[0]),
            datasets: [{
                data: values,
                backgroundColor: values.map((_, index) => index === values.length - 1 ? "#25483e" : "#91aa98"),
                borderRadius: 3,
                maxBarThickness: 31
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            plugins: {
                legend: { display: false },
                tooltip: { callbacks: {
                    title: items => months[items[0].dataIndex].month,
                    label: item => fullAmount.format(item.raw)
                } }
            },
            scales: {
                x: { grid: { display: false }, ticks: { color: "#555e55", font: axisFont } },
                y: {
                    beginAtZero: true,
                    border: { display: false },
                    grid: { color: "#e6e8df" },
                    ticks: { color: "#555e55", font: axisFont, maxTicksLimit: 5, callback: value => `${symbol} ${axisAmount.format(value)}` }
                }
            }
        }
    });
};
