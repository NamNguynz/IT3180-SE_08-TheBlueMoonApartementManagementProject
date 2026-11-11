// Trang Công nợ và nhắc nộp (PBI-019). Danh sách và quy tắc nhắc nằm ở DebtService;
// file này chỉ lọc, chọn hộ, soạn giấy báo để in và gọi API api/cong-no.
(() => {
    const rootId = 'debts';
    const printRootId = 'dbPrintRoot';
    let debtList = null;          // kết quả GET api/cong-no
    let selected = new Set();     // khóa "kỳ:hộ" của các dòng đã chọn
    let initializedRoot;

    const $ = (selector) => document.querySelector(`#${rootId} ${selector}`);
    const keyOf = (row) => `${row.billingPeriodId}:${row.householdId}`;

    const pad = (n) => String(n).padStart(2, '0');
    const formatMoney = (value) => Number(value).toLocaleString('vi-VN');
    const formatTime = (value) => value
        ? new Date(value).toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
        : '';
    const formatDate = (value) => {
        if (!value) return '';
        const [year, month, day] = value.slice(0, 10).split('-');
        return `${day}/${month}/${year}`;
    };
    const periodName = (row) => `Tháng ${pad(row.month)}/${row.year}`;
    const todayText = () => {
        const now = new Date();
        return `${pad(now.getDate())}/${pad(now.getMonth() + 1)}/${now.getFullYear()}`;
    };

    // Nhãn lấy từ datalist (trang sinh ra từ PaymentLabels.cs, DebtLabels.cs)
    const labels = (selector) => Object.fromEntries([...$(selector).options].map((o) => [o.value, o.textContent.trim()]));
    const statusLabel = (status) => labels('#dbStatusLabels')[status] ?? status;
    const channelLabel = (channel) => labels('#dbChannelLabels')[channel] ?? channel;

    const showAlert = (message, success = false, selector = '#dbAlert') => {
        const alert = $(selector);
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
        const validationErrors = data?.errors ? Object.values(data.errors).flat().join(' ') : null;
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

    // ---- Tải dữ liệu ----

    // Lỗi khi tải danh sách kỳ thì vẫn xem được "Tất cả kỳ", nên chỉ bỏ qua
    const loadPeriods = async () => {
        try {
            const response = await api('/api/ky-thu');
            if (!response.ok) return;
            const select = $('#dbPeriod');
            select.replaceChildren(select.options[0], ...(response.data ?? []).map((period) => {
                const option = document.createElement('option');
                option.value = period.id;
                option.textContent = `${periodName(period)} · hạn nộp ${formatDate(period.dueDate)}`;
                return option;
            }));
        } catch {
            // Mất mạng: loadDebts sẽ báo lỗi cho người dùng
        }
    };

    // Tải lỗi thì xóa bảng, để không còn hiện công nợ của bộ lọc cũ
    const loadDebts = async () => {
        const periodId = $('#dbPeriod').value;
        try {
            const response = await api(periodId ? `/api/cong-no?kyThu=${periodId}` : '/api/cong-no');
            if (response.status === 401) return;
            if (!response.ok) {
                debtList = null;
                render();
                showAlert(messageFor(response, 'Không tải được danh sách công nợ.'));
                return;
            }
            debtList = response.data;
            selected = new Set();
            render();
        } catch {
            debtList = null;
            render();
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    // ---- Hiển thị ----

    const visibleRows = () => {
        if (!debtList) return [];
        const status = $('#dbStatus').value;
        const overdueOnly = $('#dbOverdue').checked;
        const query = $('#dbSearch').value.trim().toLocaleLowerCase('vi');
        return debtList.rows.filter((row) =>
            (!status || row.status === status) &&
            (!overdueOnly || row.isOverdue) &&
            (!query ||
                row.apartmentCode.toLocaleLowerCase('vi').includes(query) ||
                row.building.toLocaleLowerCase('vi').includes(query) ||
                row.householdNumber.toLocaleLowerCase('vi').includes(query)));
    };

    const render = () => {
        $('#dbHouseholds').textContent = debtList ? `${debtList.householdCount} hộ` : '–';
        $('#dbTotal').textContent = debtList ? `${formatMoney(debtList.totalRemaining)} đ` : '–';
        $('#dbOverdueSummary').textContent = debtList
            ? `${debtList.overdueHouseholdCount} hộ · ${formatMoney(debtList.overdueRemaining)} đ`
            : '–';

        const rows = visibleRows();
        // Chỉ giữ lựa chọn của các dòng đang hiện: đổi bộ lọc thì hộ bị ẩn không còn được nhắc ngoài ý muốn
        const visibleKeys = new Set(rows.map(keyOf));
        selected = new Set([...selected].filter((key) => visibleKeys.has(key)));
        $('#dbRows').replaceChildren(...rows.map((row) => {
            const tr = document.createElement('tr');
            tr.classList.toggle('is-overdue', row.isOverdue);

            const checkCell = document.createElement('td');
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.dataset.key = keyOf(row);
            checkbox.checked = selected.has(keyOf(row));
            checkbox.setAttribute('aria-label', `Chọn hộ ${row.householdNumber} kỳ ${periodName(row)}`);
            checkCell.append(checkbox);

            const statusCell = document.createElement('td');
            const badge = document.createElement('span');
            badge.className = row.status === 'NopMotPhan' ? 'badge badge-warn' : 'badge badge-muted';
            badge.textContent = statusLabel(row.status);
            statusCell.append(badge);
            if (row.isOverdue) {
                const overdue = document.createElement('span');
                overdue.className = 'db-overdue';
                overdue.textContent = `Quá hạn ${row.daysOverdue} ngày`;
                statusCell.append(overdue);
            }

            const actions = document.createElement('td');
            const history = document.createElement('button');
            history.type = 'button';
            history.className = 'btn-link';
            history.dataset.action = 'history';
            history.dataset.key = keyOf(row);
            history.textContent = 'Lịch sử nhắc';
            actions.append(history);

            tr.append(
                checkCell,
                cell(periodName(row)),
                cell(formatDate(row.dueDate)),
                cell(`${row.apartmentCode} (${row.building})`),
                cell(row.householdNumber),
                cell(formatMoney(row.amountDue), 'num'),
                cell(formatMoney(row.amountPaid), 'num'),
                cell(formatMoney(row.remaining), 'num db-remaining'),
                statusCell,
                cell(row.lastRemindedAt ? `${formatTime(row.lastRemindedAt)} (${row.reminderCount} lần)` : 'Chưa nhắc'),
                actions);
            return tr;
        }));
        // Tải lỗi (debtList null) thì không hiện "không có hộ nào còn nợ", tránh hiểu nhầm là đã thu đủ
        $('#dbEmpty').hidden = rows.length > 0 || !debtList;
        $('#dbEmpty').textContent = debtList && debtList.rows.length > 0
            ? 'Không có hộ phù hợp bộ lọc.'
            : 'Không có hộ nào còn nợ.';

        syncSelectAll(rows);
        updateSelection();
    };

    const syncSelectAll = (rows = visibleRows()) => {
        $('#dbSelectAll').checked = rows.length > 0 && rows.every((row) => selected.has(keyOf(row)));
    };

    const updateSelection = () => {
        $('#dbSelectedCount').textContent = selected.size > 0 ? `Đã chọn ${selected.size} hộ` : 'Chưa chọn hộ nào';
        $('[data-action="open-remind"]').disabled = selected.size === 0;
    };

    const selectedRows = () => (debtList?.rows ?? []).filter((row) => selected.has(keyOf(row)));

    // ---- Giấy báo nhắc nộp ----

    const buildNotice = (row) => {
        const notice = document.createElement('article');
        notice.className = 'db-notice';
        const paragraph = (text, className) => {
            const p = document.createElement('p');
            p.textContent = text;
            if (className) p.className = className;
            return p;
        };
        notice.append(
            paragraph('GIẤY BÁO NHẮC NỘP PHÍ', 'db-notice-title'),
            paragraph(`Kính gửi: Hộ ${row.householdNumber} – Căn hộ ${row.apartmentCode} (${row.building})`),
            paragraph(
                `Ban quản trị chung cư BlueMoon thông báo: theo kỳ thu ${periodName(row).toLocaleLowerCase('vi')} ` +
                `(hạn nộp ${formatDate(row.dueDate)}), hộ gia đình còn thiếu ${formatMoney(row.remaining)} đồng ` +
                `(phải thu ${formatMoney(row.amountDue)} đồng, đã nộp ${formatMoney(row.amountPaid)} đồng).`),
            paragraph(
                (row.isOverdue ? `Khoản này đã quá hạn ${row.daysOverdue} ngày. ` : '') +
                'Đề nghị hộ gia đình hoàn thành việc nộp phí tại văn phòng Ban quản trị hoặc chuyển khoản theo thông tin ' +
                `đã được thông báo, ghi rõ số hộ khẩu ${row.householdNumber} trong nội dung chuyển khoản.`),
            paragraph('Nếu hộ đã nộp, xin bỏ qua thông báo này.'),
            paragraph(`Ngày ${todayText()}`, 'db-notice-date'));
        return notice;
    };

    const openRemind = () => {
        const rows = selectedRows();
        if (rows.length === 0) return;
        $('#dbRemindForm').reset();
        $('#dbRemindAlert').hidden = true;
        $('#dbRemindTitle').textContent = `Nhắc nộp ${rows.length} hộ`;
        $('#dbNotices').replaceChildren(...rows.map(buildNotice));
        $('#dbRemindDialog').hidden = false;
        $('#dbRemindForm').elements.namedItem('channel').focus();
    };

    // In: chép giấy báo ra một vùng ở cấp <body>, CSS in chỉ hiện vùng đó (mỗi hộ một trang), in xong thì gỡ
    const printNotices = () => {
        document.getElementById(printRootId)?.remove();
        const printRoot = document.createElement('div');
        printRoot.id = printRootId;
        printRoot.append(...[...$('#dbNotices').children].map((notice) => notice.cloneNode(true)));
        document.body.append(printRoot);
        document.body.classList.add('printing-notices');
        const cleanUp = () => {
            document.body.classList.remove('printing-notices');
            printRoot.remove();
            window.removeEventListener('afterprint', cleanUp);
        };
        window.addEventListener('afterprint', cleanUp);
        window.print();
    };

    const onRemindSubmit = async (form) => {
        if (!form.reportValidity()) return;
        const rows = selectedRows();
        const button = form.querySelector('[type="submit"]');
        button.disabled = true;
        try {
            const response = await api('/api/cong-no/nhac-nop', {
                method: 'POST',
                body: {
                    items: rows.map((row) => ({ billingPeriodId: row.billingPeriodId, householdId: row.householdId })),
                    channel: form.elements.namedItem('channel').value,
                    note: form.elements.namedItem('note').value.trim() || null
                }
            });
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không ghi nhận được lần nhắc.'), false, '#dbRemindAlert');
                return;
            }
            $('#dbRemindDialog').hidden = true;
            await loadDebts();
            showAlert(`Đã ghi nhận nhắc nộp ${response.data.length} hộ (${channelLabel(response.data[0]?.channel)}).`, true);
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.', false, '#dbRemindAlert');
        } finally {
            button.disabled = false;
        }
    };

    // ---- Lịch sử nhắc ----

    const openHistory = async (key) => {
        const row = debtList?.rows.find((item) => keyOf(item) === key);
        if (!row) return;
        try {
            const response = await api(`/api/cong-no/nhac-nop?kyThu=${row.billingPeriodId}&hoGiaDinh=${row.householdId}`);
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không tải được lịch sử nhắc.'));
                return;
            }
            $('#dbHistoryTitle').textContent = `Lịch sử nhắc hộ ${row.householdNumber} · kỳ ${periodName(row).toLocaleLowerCase('vi')}`;
            $('#dbHistoryRows').replaceChildren(...response.data.map((reminder) => {
                const tr = document.createElement('tr');
                tr.append(
                    cell(formatTime(reminder.remindedAt)),
                    cell(channelLabel(reminder.channel)),
                    cell(formatMoney(reminder.remaining), 'num'),
                    cell(reminder.note ?? ''),
                    cell(reminder.remindedBy));
                return tr;
            }));
            $('#dbHistoryEmpty').hidden = response.data.length > 0;
            $('#dbHistoryDialog').hidden = false;
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    // ---- Sự kiện ----

    const onClick = async (event) => {
        const button = event.target.closest('[data-action]');
        if (!button || !document.getElementById(rootId).contains(button)) return;

        switch (button.dataset.action) {
            case 'open-remind':
                openRemind();
                break;
            case 'print':
                printNotices();
                break;
            case 'history':
                await openHistory(button.dataset.key);
                break;
            case 'close':
                button.closest('.db-dialog-backdrop').hidden = true;
                break;
        }
    };

    const onChange = (event) => {
        const target = event.target;
        // Tích chọn chỉ cập nhật ô và số đếm, không vẽ lại bảng, để không mất focus bàn phím
        if (target.matches('#dbRows input[type="checkbox"]')) {
            if (target.checked) selected.add(target.dataset.key);
            else selected.delete(target.dataset.key);
            syncSelectAll();
            updateSelection();
        } else if (target.id === 'dbSelectAll') {
            for (const row of visibleRows()) {
                if (target.checked) selected.add(keyOf(row));
                else selected.delete(keyOf(row));
            }
            for (const box of $('#dbRows').querySelectorAll('input[type="checkbox"]')) box.checked = target.checked;
            updateSelection();
        } else if (target.id === 'dbPeriod') {
            void loadDebts();
        } else if (target.id === 'dbStatus' || target.id === 'dbOverdue') {
            render();
        }
    };

    const init = () => {
        const root = document.getElementById(rootId);
        if (!root || root === initializedRoot) return;
        initializedRoot = root;
        debtList = null;
        selected = new Set();
        root.addEventListener('click', onClick);
        root.addEventListener('change', onChange);
        root.addEventListener('submit', (event) => {
            if (event.target.getAttribute('id') !== 'dbRemindForm') return;
            event.preventDefault();
            void onRemindSubmit(event.target);
        });
        $('#dbSearch').addEventListener('input', render);
        void loadPeriods();
        void loadDebts();
    };

    init();
    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();
