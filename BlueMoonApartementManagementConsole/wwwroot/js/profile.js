(() => {
    if (window.__profileBound) return;
    window.__profileBound = true;

    const el = (id) => document.getElementById(id);

    const showAlert = (message, ok) => {
        const box = el('pwAlert');
        box.className = 'alert' + (ok ? ' alert-success' : '');
        box.textContent = message;
        box.hidden = false;
    };

    const onSubmit = async (e) => {
        if (e.target.id !== 'passwordForm') return;
        e.preventDefault();

        const form = e.target;
        const current = form.elements.namedItem('current').value;
        const next = form.elements.namedItem('next').value;
        const confirm = form.elements.namedItem('confirm').value;
        const btn = form.querySelector('button[type="submit"]');

        if (next !== confirm) {
            showAlert('Mật khẩu nhập lại không khớp.', false);
            return;
        }

        btn.disabled = true;
        try {
            const res = await fetch('/api/ho-so/doi-mat-khau', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({ currentPassword: current, newPassword: next })
            });

            if (res.status === 401) {
                location.assign('/login');
                return;
            }

            if (res.ok) {
                form.reset();
                showAlert('Đã đổi mật khẩu. Đang chuyển đến trang chủ...', true);
                setTimeout(() => location.assign('/'), 800);
                return;
            }

            let message = 'Không đổi được mật khẩu. Vui lòng thử lại.';
            try {
                const err = await res.json();
                if (err?.message) message = err.message;
            } catch { /* không phải JSON */ }
            showAlert(message, false);
        } catch {
            showAlert('Lỗi kết nối. Vui lòng kiểm tra mạng và thử lại.', false);
        } finally {
            btn.disabled = false;
        }
    };

    document.addEventListener('submit', onSubmit);
})();