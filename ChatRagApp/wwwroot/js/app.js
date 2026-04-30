// Scroll a container to bottom
function scrollToBottom(elementId) {
    const el = document.getElementById(elementId);
    if (el) el.scrollTop = el.scrollHeight;
}

window.themeManager = {
    getPreferredTheme() {
        try {
            const storedTheme = window.localStorage.getItem('chatrag-theme');
            if (storedTheme === 'dark' || storedTheme === 'light') {
                return storedTheme;
            }
        } catch {
        }

        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    },

    applyTheme(theme) {
        const resolvedTheme = theme === 'light' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', resolvedTheme);

        try {
            window.localStorage.setItem('chatrag-theme', resolvedTheme);
        } catch {
        }
    },

    initialize() {
        const theme = this.getPreferredTheme();
        this.applyTheme(theme);
        return theme === 'dark';
    }
};

// Idle timer — logs user out after 5 minutes of inactivity
(function () {
    const IDLE_MS = 5 * 60 * 1000;
    let lastActivity = Date.now();

    ['mousemove', 'keydown', 'click', 'scroll', 'touchstart'].forEach(ev =>
        document.addEventListener(ev, () => { lastActivity = Date.now(); }, { passive: true })
    );

    setInterval(() => {
        if (Date.now() - lastActivity > IDLE_MS) {
            window.location.href = '/account/logout';
        }
    }, 30_000);
})();

// Chart.js token usage chart renderer
function renderTokenChart(canvasId, labels, inputData, outputData) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (ctx._chartInstance) {
        ctx._chartInstance.destroy();
    }

    if (typeof Chart === 'undefined') return;

    ctx._chartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels.length > 0 ? labels : ['No data'],
            datasets: [
                {
                    label: 'Input Tokens',
                    data: inputData.length > 0 ? inputData : [0],
                    backgroundColor: 'rgba(54, 162, 235, 0.7)',
                },
                {
                    label: 'Output Tokens',
                    data: outputData.length > 0 ? outputData : [0],
                    backgroundColor: 'rgba(75, 192, 192, 0.7)',
                }
            ]
        },
        options: {
            responsive: true,
            plugins: {
                legend: { position: 'top' },
            },
            scales: {
                y: { beginAtZero: true }
            }
        }
    });
}
