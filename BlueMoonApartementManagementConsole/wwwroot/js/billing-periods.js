// Trang Kỳ thu (PBI-014). Quy tắc lập bảng kê nằm ở BillingPeriodService;
// file này chỉ hiện form lập kỳ, danh sách kỳ thu và bảng kê, gọi API api/ky-thu.
(() => {
    const rootId = 'billingPeriods';
    // Nhãn giống trang Khoản thu (fee-items.js)
    const CYCLE_LABELS = { MotLan: 'Một lần', Thang: 'Hàng tháng', Nam: 'Hàng năm' };
    const BASIS_LABELS = { DienTich: 'Diện tích', NhanKhau: 'Nhân khẩu', Ho: 'Theo hộ', CoDinh: 'Cố định' };

    let periods = [];
    let detail = null;
    let initializedRoot;

    const $ = (selector) => document.querySelector(`#${rootId} ${selector}`);
    const formValue = (form, name) => form.elements.namedItem(name).value;

    const pad = (n) => String(n).padStart(2, '0');
    const isoDate = (date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    const today = () => isoDate(new Date());
    // Ngày thuần yyyy-MM-dd (DateOnly), hiển thị dd/MM/yyyy
    const formatDate = (value) => {
        if (!value) return '';
        const [year, month, day] = value.slice(0, 10).split('-');
        return `${day}/${month}/${year}`;
    };
    // Thời điểm (createdAt, lưu UTC): đổi sang giờ máy người dùng
    const formatTime = (value) => value
        ? new Date(value).toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
        : '';
    const formatMoney = (value) => Number(value).toLocaleString('vi-VN');
    const periodName = (period) => `Tháng ${pad(period.month)}/${period.year}`;

    const showAlert = (message, success = false) => {
        const alert = $('#bpAlert');
        alert.textContent = message;
        alert.classList.toggle('alert-success', success);
        alert.hidden = false;
        alert.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    };

    const api = async (url, options = {}) => {
        const response = await fetch(url, {
            method: options.method ?? 'GET',
            credentials: 'same-origin',
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {})
            },
            body: options.body ? JSON.stringify(options.body) : undefined
        });

        if (response.status === 401) {
            location.assign('/login');
            return { ok: false, status: 401, data: null };
        }

        let data = null;
        try {
            data = await response.json();
        } catch {
            // Phản hồi không có JSON
        }
        return { ok: response.ok, status: response.status, data };
    };

    const messageFor = (response, fallback) => {
        const data = response.data;
        const validationErrors = data?.errors
            ? Object.values(data.errors).flat().join(' ')
            : null;
        const message = data?.message ?? validationErrors ?? data?.title;
        if (message) return message;
        if (response.status === 403) return 'Tài khoản hiện tại không có quyền tài chính để thực hiện thao tác.';
        if (response.status >= 500) return `Máy chủ gặp lỗi khi xử lý yêu cầu (${response.status}).`;
        return `${fallback} (HTTP ${response.status}).`;
    };

    const cell = (text, className) => {
        const td = document.createElement('td');
        td.textContent = text;
        if (className) td.className = className;
        return td;
    };

    // ---- Danh sách kỳ thu ----

    const renderPeriods = () => {
        $('#bpPeriods').replaceChildren(...periods.map((period) => {
            const row = document.createElement('tr');
            row.classList.toggle('is-selected', detail?.id === period.id);
            const actions = document.createElement('td');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn-link';
            button.dataset.action = 'view-period';
            button.dataset.id = period.id;
            button.textContent = 'Xem bảng kê';
            actions.append(button);
            row.append(
                cell(periodName(period)),
                cell(formatDate(period.chargeDate)),
                cell(formatDate(period.dueDate)),
                cell(String(period.itemCount), 'num'),
                cell(String(period.householdCount), 'num'),
                cell(formatMoney(period.totalAmount), 'num'),
                cell(`${period.createdBy} · ${formatTime(period.createdAt)}`),
                actions);
            return row;
        }));
        $('#bpPeriodsEmpty').hidden = periods.length > 0;
    };

    const loadPeriods = async () => {
        try {
            const response = await api('/api/ky-thu');
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không tải được danh sách kỳ thu.'));
                return;
            }
            periods = response.data ?? [];
            renderPeriods();
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    // ---- Bảng kê của kỳ đang chọn ----

    const viewPeriod = async (id) => {
        try {
            const response = await api(`/api/ky-thu/${id}`);
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không tải được bảng kê.'));
                return;
            }
            showDetail(response.data);
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    const showDetail = (period) => {
        detail = period;
        $('#bpSearch').value = '';
        $('#bpDetail').hidden = false;
        $('#bpDetailTitle').textContent = `Bảng kê phải thu ${periodName(period).toLocaleLowerCase('vi')}`;
        $('#bpDetailMeta').textContent = [
            `Ngày tính phí ${formatDate(period.chargeDate)}`,
            `Hạn nộp ${formatDate(period.dueDate)}`,
            `${period.households.length} hộ`,
            `Lập bởi ${period.createdBy} lúc ${formatTime(period.createdAt)}`,
            period.note ? `Ghi chú: ${period.note}` : ''
        ].filter(Boolean).join(' · ');
        $('#bpDetailTotal').textContent = `${formatMoney(period.totalAmount)} đ`;
        renderItems();
        renderBills();
        renderPeriods();
        $('#bpDetail').scrollIntoView({ behavior: 'smooth', block: 'start' });
    };

    const renderItems = () => {
        $('#bpItems').replaceChildren(...detail.items.map((item) => {
            const row = document.createElement('tr');
            row.append(
                cell(item.code),
                cell(item.name),
                cell(CYCLE_LABELS[item.feeCycle] ?? item.feeCycle),
                cell(`${BASIS_LABELS[item.calculationBasis] ?? item.calculationBasis} (${item.unit})`),
                cell(formatMoney(item.rate), 'num'),
                cell(formatDate(item.rateEffectiveFrom)),
                cell(formatMoney(item.totalAmount), 'num'));
            return row;
        }));
    };

    // Mỗi hộ một dòng, mỗi khoản thu một cột; ô ghi số tiền, rê chuột xem cách tính (cơ sở tính × đơn giá)
    const renderBills = () => {
        const query = $('#bpSearch').value.trim().toLocaleLowerCase('vi');
        const households = detail.households.filter((household) =>
            !query ||
            household.apartmentCode.toLocaleLowerCase('vi').includes(query) ||
            household.building.toLocaleLowerCase('vi').includes(query) ||
            household.householdNumber.toLocaleLowerCase('vi').includes(query));

        const headRow = document.createElement('tr');
        for (const text of ['Căn hộ', 'Tòa', 'Số hộ khẩu']) {
            const th = document.createElement('th');
            th.textContent = text;
            headRow.append(th);
        }
        for (const item of detail.items) {
            const th = document.createElement('th');
            th.className = 'num';
            th.textContent = item.code;
            th.title = item.name;
            headRow.append(th);
        }
        const totalHead = document.createElement('th');
        totalHead.className = 'num';
        totalHead.textContent = 'Phải thu (đ)';
        headRow.append(totalHead);
        $('#bpBillHead').replaceChildren(headRow);

        $('#bpBills').replaceChildren(...households.map((household) => {
            const row = document.createElement('tr');
            row.append(cell(household.apartmentCode), cell(household.building), cell(household.householdNumber));
            for (const item of detail.items) {
                const line = household.lines.find((l) => l.feeItemId === item.feeItemId);
                const td = cell(line ? formatMoney(line.amount) : '—', 'num');
                if (line) {
                    td.title = `${Number(line.quantity).toLocaleString('vi-VN')} ${item.unit} × ${formatMoney(line.rate)} đ`;
                }
                row.append(td);
            }
            row.append(cell(formatMoney(household.totalAmount), 'num bp-total'));
            return row;
        }));
        $('#bpBillsEmpty').hidden = households.length > 0;

        // Dòng tổng luôn là tổng cả kỳ, không đổi theo ô tìm kiếm
        const footRow = document.createElement('tr');
        const label = cell(`Tổng cả kỳ (${detail.households.length} hộ)`);
        label.colSpan = 3;
        footRow.append(label);
        for (const item of detail.items) {
            footRow.append(cell(formatMoney(item.totalAmount), 'num'));
        }
        footRow.append(cell(formatMoney(detail.totalAmount), 'num bp-total'));
        $('#bpBillFoot').replaceChildren(footRow);
    };

    // ---- Form lập kỳ thu ----

    const lastDayOfMonth = (year, month) => isoDate(new Date(year, month, 0));

    const loadFeeItemOptions = async () => {
        const container = $('#bpFeeItemOptions');
        container.replaceChildren();
        const response = await api('/api/khoan-thu');
        if (response.status === 401) return;
        if (!response.ok) {
            showAlert(messageFor(response, 'Không tải được danh sách khoản thu.'));
            return;
        }

        // Chỉ khoản bắt buộc, áp dụng toàn chung cư (server cũng kiểm); khoản hàng tháng được chọn sẵn
        const eligible = (response.data ?? []).filter((item) => item.feeType === 'BatBuoc' && item.feeScope === 'Chung');
        const now = today();
        container.replaceChildren(...eligible.map((item) => {
            const label = document.createElement('label');
            label.className = 'bp-fee-item';
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.name = 'feeItemIds';
            checkbox.value = item.id;
            checkbox.checked = item.feeCycle === 'Thang';
            const current = item.rates.find((r) => r.effectiveFrom <= now && (!r.effectiveTo || r.effectiveTo > now));
            const text = document.createElement('span');
            text.textContent = [
                `${item.code} – ${item.name}`,
                CYCLE_LABELS[item.feeCycle] ?? item.feeCycle,
                current ? `${formatMoney(current.rate)} đ/${item.unit}` : 'chưa có đơn giá hiện hành'
            ].join(' · ');
            label.append(checkbox, text);
            return label;
        }));
        $('#bpFeeItemEmpty').hidden = eligible.length > 0;
    };

    const openCreate = async () => {
        const form = $('#bpCreateForm');
        form.reset();
        const now = new Date();
        form.elements.namedItem('period').value = `${now.getFullYear()}-${pad(now.getMonth() + 1)}`;
        form.elements.namedItem('period').max = form.elements.namedItem('period').value;
        form.elements.namedItem('chargeDate').value = today();
        form.elements.namedItem('chargeDate').max = today();
        form.elements.namedItem('dueDate').value = lastDayOfMonth(now.getFullYear(), now.getMonth() + 1);
        onPeriodChange(form);
        $('#bpCreate').hidden = false;
        try {
            await loadFeeItemOptions();
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
        form.elements.namedItem('period').focus();
    };

    // Ngày tính phí nằm trong tháng của kỳ và không sau hôm nay; hạn nộp không trước ngày tính phí (server cũng kiểm).
    // Đổi tháng (ví dụ lập bù kỳ cũ) thì đưa ngày tính phí về trong tháng đó.
    const onPeriodChange = (form) => {
        const period = formValue(form, 'period');
        const chargeDate = form.elements.namedItem('chargeDate');
        if (period) {
            const [year, month] = period.split('-').map(Number);
            const monthEnd = lastDayOfMonth(year, month);
            chargeDate.min = `${period}-01`;
            chargeDate.max = monthEnd < today() ? monthEnd : today();
            if (chargeDate.value < chargeDate.min || chargeDate.value > chargeDate.max) chargeDate.value = chargeDate.max;
        }
        form.elements.namedItem('dueDate').min = chargeDate.value;
    };

    const onCreateSubmit = async (form) => {
        if (!form.reportValidity()) return;
        const feeItemIds = [...form.querySelectorAll('input[name="feeItemIds"]:checked')].map((box) => Number(box.value));
        if (feeItemIds.length === 0) {
            showAlert('Hãy chọn ít nhất một khoản thu.');
            return;
        }

        const [year, month] = formValue(form, 'period').split('-').map(Number);
        const body = {
            year,
            month,
            chargeDate: formValue(form, 'chargeDate'),
            dueDate: formValue(form, 'dueDate'),
            feeItemIds,
            note: formValue(form, 'note').trim() || null
        };

        const button = form.querySelector('[type="submit"]');
        button.disabled = true;
        button.textContent = 'Đang lập...';
        try {
            const response = await api('/api/ky-thu', { method: 'POST', body });
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không lập được kỳ thu.'));
                return;
            }

            $('#bpCreate').hidden = true;
            await loadPeriods();
            showDetail(response.data);
            showAlert(`Đã lập bảng kê ${periodName(response.data).toLocaleLowerCase('vi')}: ${response.data.households.length} hộ, tổng ${formatMoney(response.data.totalAmount)} đ.`, true);
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        } finally {
            button.disabled = false;
            button.textContent = 'Lập bảng kê';
        }
    };

    const onClick = async (event) => {
        const button = event.target.closest('[data-action]');
        if (!button || !document.getElementById(rootId).contains(button)) return;

        switch (button.dataset.action) {
            case 'toggle-create':
                if ($('#bpCreate').hidden) await openCreate();
                else $('#bpCreate').hidden = true;
                break;
            case 'view-period':
                await viewPeriod(Number(button.dataset.id));
                break;
        }
    };

    const init = () => {
        const root = document.getElementById(rootId);
        if (!root || root === initializedRoot) return;
        initializedRoot = root;
        periods = [];
        detail = null;
        root.addEventListener('click', onClick);
        root.addEventListener('submit', (event) => {
            if (event.target.getAttribute('id') !== 'bpCreateForm') return;
            event.preventDefault();
            void onCreateSubmit(event.target);
        });
        root.addEventListener('change', (event) => {
            const form = event.target.form;
            if (form?.getAttribute('id') === 'bpCreateForm' && ['period', 'chargeDate'].includes(event.target.name)) {
                onPeriodChange(form);
            }
        });
        $('#bpSearch').addEventListener('input', () => detail && renderBills());
        void loadPeriods();
    };

    init();
    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();
