// Trang Kỳ thu (PBI-014). Quy tắc lập bảng kê nằm ở BillingPeriodService;
// file này chỉ hiện form lập kỳ, pop-up chọn hộ, danh sách kỳ thu và bảng kê, gọi API api/ky-thu.
(() => {
    const rootId = 'billingPeriods';
    // Nhãn giống trang Khoản thu (fee-items.js)
    const CYCLE_LABELS = { MotLan: 'Một lần', Thang: 'Hàng tháng', Nam: 'Hàng năm' };
    const BASIS_LABELS = { DienTich: 'Diện tích', NhanKhau: 'Nhân khẩu', Ho: 'Theo hộ', CoDinh: 'Cố định' };

    let periods = [];
    let detail = null;

    // Listener gắn trên document để không mất khi Blazor thay DOM; mountedRoot dùng để nhận ra lần mount mới.
    let bound = false;
    let mountedRoot = null;
    let rootObserver = null;

    const inRoot = (node) => Boolean(node && document.getElementById(rootId)?.contains(node));

    // Form lập kỳ: khoản thu đang hiện (theo id), danh sách hộ đã chọn của từng khoản chỉ định hộ (id → Set id hộ),
    // danh sách hộ để chọn (tải khi mở pop-up lần đầu), và khoản đang chọn hộ trong pop-up
    let feeItems = new Map();
    let targets = new Map();
    let householdOptions = null;
    let editing = null;

    const $ = (selector) => document.querySelector(`#${rootId} ${selector}`);
    const formValue = (form, name) => form.elements.namedItem(name).value;

    const pad = (n) => String(n).padStart(2, '0');
    const isoDate = (date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    const monthValue = (date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}`;
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
        feeItems = new Map();
        const response = await api('/api/khoan-thu');
        if (response.status === 401) return;
        if (!response.ok) {
            showAlert(messageFor(response, 'Không tải được danh sách khoản thu.'));
            return;
        }

        // Chỉ khoản bắt buộc (server cũng kiểm). Khoản toàn chung cư hàng tháng được chọn sẵn;
        // khoản chỉ định hộ không chọn sẵn, tích vào thì mở pop-up chọn hộ.
        const eligible = (response.data ?? []).filter((item) =>
            item.feeType === 'BatBuoc' && (item.feeScope === 'Chung' || item.feeScope === 'ChiDinh'));
        const now = today();
        container.replaceChildren(...eligible.map((item) => {
            feeItems.set(item.id, item);
            const targeted = item.feeScope === 'ChiDinh';

            const row = document.createElement('div');
            row.className = 'bp-fee-item-row';
            const label = document.createElement('label');
            label.className = 'bp-fee-item';
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.name = 'feeItemIds';
            checkbox.value = item.id;
            checkbox.checked = !targeted && item.feeCycle === 'Thang';
            const current = item.rates.find((r) => r.effectiveFrom <= now && (!r.effectiveTo || r.effectiveTo > now));
            const text = document.createElement('span');
            text.textContent = [
                `${item.code} – ${item.name}`,
                targeted ? 'Chỉ định hộ' : (CYCLE_LABELS[item.feeCycle] ?? item.feeCycle),
                current ? `${formatMoney(current.rate)} đ/${item.unit}` : 'chưa có đơn giá hiện hành'
            ].join(' · ');
            label.append(checkbox, text);
            row.append(label);

            if (targeted) {
                // Dòng tóm tắt các hộ đã chọn, hiện sau khi xác nhận ở pop-up
                const summary = document.createElement('div');
                summary.className = 'bp-target-summary';
                summary.dataset.feeItemId = item.id;
                summary.hidden = true;
                row.append(summary);
            }
            return row;
        }));
        $('#bpFeeItemEmpty').hidden = eligible.length > 0;
    };

    const openCreate = async () => {
        const form = $('#bpCreateForm');
        form.reset();
        targets = new Map();
        householdOptions = null; // tải lại khi mở pop-up, vì danh sách hộ có thể đã đổi
        const now = new Date();
        const period = form.elements.namedItem('period');
        period.value = monthValue(now);
        // Lập trước được kỳ của tháng sau, không xa hơn (server cũng kiểm)
        period.max = monthValue(new Date(now.getFullYear(), now.getMonth() + 1, 1));
        form.elements.namedItem('chargeDate').value = today();
        form.elements.namedItem('dueDate').value = lastDayOfMonth(now.getFullYear(), now.getMonth() + 1);
        onPeriodChange(form);
        $('#bpCreate').hidden = false;
        try {
            await loadFeeItemOptions();
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
        period.focus();
    };

    // Ngày tính phí nằm trong tháng của kỳ; hạn nộp không trước ngày tính phí (server cũng kiểm).
    // Đổi tháng (lập bù kỳ cũ hoặc lập trước kỳ tháng sau) thì đưa ngày tính phí về trong tháng đó.
    const onPeriodChange = (form) => {
        const period = formValue(form, 'period');
        const chargeDate = form.elements.namedItem('chargeDate');
        const dueDate = form.elements.namedItem('dueDate');
        if (period) {
            const [year, month] = period.split('-').map(Number);
            const monthEnd = lastDayOfMonth(year, month);
            chargeDate.min = `${period}-01`;
            chargeDate.max = monthEnd;
            if (chargeDate.value < chargeDate.min) chargeDate.value = chargeDate.min;
            if (chargeDate.value > chargeDate.max) chargeDate.value = chargeDate.max;
            // Hạn nộp đang trước ngày tính phí (thường do vừa đổi sang tháng sau) thì lấy cuối tháng của kỳ
            if (!dueDate.value || dueDate.value < chargeDate.value) dueDate.value = monthEnd;
        }
        dueDate.min = chargeDate.value;
    };

    const onCreateSubmit = async (form) => {
        if (!form.reportValidity()) return;
        const feeItemIds = [...form.querySelectorAll('input[name="feeItemIds"]:checked')].map((box) => Number(box.value));
        if (feeItemIds.length === 0) {
            showAlert('Hãy chọn ít nhất một khoản thu.');
            return;
        }

        // Khoản chỉ định hộ gửi kèm danh sách hộ đã chọn (pop-up bắt chọn ít nhất một hộ; server cũng kiểm)
        const targetedFeeItems = [];
        for (const feeItemId of feeItemIds) {
            const item = feeItems.get(feeItemId);
            if (item?.feeScope !== 'ChiDinh') continue;
            const selected = targets.get(feeItemId);
            if (!selected?.size) {
                showAlert(`Khoản thu ${item.code} áp dụng cho hộ chỉ định: hãy chọn ít nhất một hộ.`);
                return;
            }
            targetedFeeItems.push({ feeItemId, householdIds: [...selected] });
        }

        const [year, month] = formValue(form, 'period').split('-').map(Number);
        const body = {
            year,
            month,
            chargeDate: formValue(form, 'chargeDate'),
            dueDate: formValue(form, 'dueDate'),
            feeItemIds,
            targetedFeeItems,
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

            form.reset();
            feeItems.clear();
            targets.clear();
            householdOptions = null;
            editing = null;
            detail = null;
            $('#bpCreate').hidden = true;
            $('#bpDetail').hidden = true;
            const targetDialog = $('#bpTargetDialog');
            if (targetDialog?.open) targetDialog.close();
            await loadPeriods();
            showAlert(`Đã lập bảng kê ${periodName(response.data).toLocaleLowerCase('vi')}: ${response.data.households.length} hộ, tổng ${formatMoney(response.data.totalAmount)} đ.`, true);
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        } finally {
            button.disabled = false;
            button.textContent = 'Lập bảng kê';
        }
    };

    // ---- Pop-up chọn hộ cho khoản thu chỉ định ----

    const feeCheckbox = (feeItemId) => $(`input[name="feeItemIds"][value="${feeItemId}"]`);

    const loadHouseholdOptions = async () => {
        if (householdOptions) return true;
        const response = await api('/api/ky-thu/ho-gia-dinh');
        if (response.status === 401) return false;
        if (!response.ok) {
            showAlert(messageFor(response, 'Không tải được danh sách hộ.'));
            return false;
        }
        householdOptions = response.data ?? [];
        return true;
    };

    // Tích khoản chỉ định hộ thì mở pop-up chọn hộ; bỏ tích thì bỏ luôn danh sách hộ đã chọn
    const onFeeItemToggle = async (checkbox) => {
        const feeItemId = Number(checkbox.value);
        if (feeItems.get(feeItemId)?.feeScope !== 'ChiDinh') return;
        if (checkbox.checked) {
            await openTargets(feeItemId);
        } else {
            targets.delete(feeItemId);
            renderTargetSummary(feeItemId);
        }
    };

    const openTargets = async (feeItemId) => {
        // Pop-up đang mở (hoặc đang tải danh sách hộ cho khoản khác, khi tích liền hai khoản): bỏ qua lần này,
        // và bỏ tích khoản vừa tích nếu nó chưa có danh sách hộ
        if (editing) {
            if (!targets.has(feeItemId)) feeCheckbox(feeItemId).checked = false;
            return;
        }
        // Sửa trên bản sao: bấm Hủy thì danh sách đã xác nhận trước đó giữ nguyên
        editing = { feeItemId, selected: new Set(targets.get(feeItemId) ?? []) };

        let loaded = false;
        try {
            loaded = await loadHouseholdOptions();
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
        if (!loaded) {
            // Không mở được pop-up thì không để khoản chỉ định ở trạng thái tích mà chưa có hộ
            editing = null;
            if (!targets.has(feeItemId)) feeCheckbox(feeItemId).checked = false;
            return;
        }

        const item = feeItems.get(feeItemId);
        $('#bpTargetTitle').textContent = `Chọn hộ phải nộp: ${item.code} – ${item.name}`;
        $('#bpTargetSearch').value = '';
        renderTargetList();
        $('#bpTargetDialog').showModal();
        $('#bpTargetSearch').focus();
    };

    const visibleHouseholds = () => {
        const query = $('#bpTargetSearch').value.trim().toLocaleLowerCase('vi');
        return householdOptions.filter((household) =>
            !query ||
            household.apartmentCode.toLocaleLowerCase('vi').includes(query) ||
            household.householdNumber.toLocaleLowerCase('vi').includes(query));
    };

    const renderTargetList = () => {
        const households = visibleHouseholds();
        $('#bpTargetList').replaceChildren(...households.map((household) => {
            const label = document.createElement('label');
            label.className = 'bp-target-option';
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.name = 'targetHousehold';
            checkbox.value = household.householdId;
            checkbox.checked = editing.selected.has(household.householdId);
            const text = document.createElement('span');
            text.textContent = `${household.apartmentCode} · Hộ khẩu ${household.householdNumber}`;
            label.append(checkbox, text);
            return label;
        }));
        $('#bpTargetEmpty').hidden = households.length > 0;
        updateTargetCount();
    };

    // Đếm cả hộ đã chọn nhưng đang bị ẩn bởi ô tìm kiếm. Chưa chọn hộ nào thì không cho xác nhận.
    const updateTargetCount = () => {
        const count = editing.selected.size;
        $('#bpTargetCount').textContent = count > 0 ? `Đã chọn ${count} hộ` : 'Chưa chọn hộ nào';
        $('[data-action="confirm-targets"]').disabled = count === 0;

        const households = visibleHouseholds();
        const selectedVisible = households.filter((h) => editing.selected.has(h.householdId)).length;
        const all = $('#bpTargetAll');
        all.disabled = households.length === 0;
        all.checked = households.length > 0 && selectedVisible === households.length;
        all.indeterminate = selectedVisible > 0 && selectedVisible < households.length;
    };

    const onTargetToggle = (checkbox) => {
        const householdId = Number(checkbox.value);
        if (checkbox.checked) editing.selected.add(householdId);
        else editing.selected.delete(householdId);
        updateTargetCount();
    };

    // "Chọn tất cả" chỉ tác động lên các hộ đang hiện theo ô tìm kiếm
    const onTargetAllToggle = (checkbox) => {
        for (const household of visibleHouseholds()) {
            if (checkbox.checked) editing.selected.add(household.householdId);
            else editing.selected.delete(household.householdId);
        }
        renderTargetList();
    };

    // Dòng tóm tắt dưới khoản chỉ định: số hộ đã chọn, vài mã căn đầu tiên, nút sửa danh sách
    const renderTargetSummary = (feeItemId) => {
        const summary = $(`.bp-target-summary[data-fee-item-id="${feeItemId}"]`);
        if (!summary) return;
        const selected = targets.get(feeItemId);
        summary.hidden = !selected;
        if (!selected) {
            summary.replaceChildren();
            return;
        }

        const codes = (householdOptions ?? [])
            .filter((household) => selected.has(household.householdId))
            .map((household) => household.apartmentCode);
        const text = document.createElement('span');
        text.textContent = `Đã chọn ${selected.size} hộ: ${codes.slice(0, 5).join(', ')}${codes.length > 5 ? ', …' : ''}`;
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn-link';
        button.dataset.action = 'edit-targets';
        button.dataset.id = feeItemId;
        button.textContent = 'Sửa danh sách hộ';
        summary.replaceChildren(text, button);
    };

    const confirmTargets = () => {
        if (!editing || editing.selected.size === 0) return;
        targets.set(editing.feeItemId, new Set(editing.selected));
        renderTargetSummary(editing.feeItemId);
        closeTargets();
    };

    // Hủy (nút Hủy hoặc phím Esc): khoản chưa từng có danh sách hộ thì bỏ tích, không để khoản chỉ định không có hộ
    const cancelTargets = () => {
        if (editing && !targets.has(editing.feeItemId)) feeCheckbox(editing.feeItemId).checked = false;
        closeTargets();
    };

    const closeTargets = () => {
        editing = null;
        const dialog = $('#bpTargetDialog');
        if (dialog.open) dialog.close();
    };

    // ---- Sự kiện ----

    const onClick = async (event) => {
        const button = event.target.closest('[data-action]');
        if (!button || !inRoot(button)) return;

        switch (button.dataset.action) {
            case 'toggle-create':
                if ($('#bpCreate').hidden) await openCreate();
                else $('#bpCreate').hidden = true;
                break;
            case 'view-period':
                await viewPeriod(Number(button.dataset.id));
                break;
            case 'edit-targets':
                await openTargets(Number(button.dataset.id));
                break;
            case 'confirm-targets':
                confirmTargets();
                break;
            case 'cancel-targets':
                cancelTargets();
                break;
        }
    };

    const onChange = (event) => {
        const target = event.target;
        if (!inRoot(target)) return;
        if (target.form?.getAttribute('id') === 'bpCreateForm') {
            if (['period', 'chargeDate'].includes(target.name)) onPeriodChange(target.form);
            if (target.name === 'feeItemIds') void onFeeItemToggle(target);
            return;
        }
        if (!editing) return;
        if (target.name === 'targetHousehold') onTargetToggle(target);
        else if (target.getAttribute('id') === 'bpTargetAll') onTargetAllToggle(target);
    };

    const onSubmit = (event) => {
        if (!inRoot(event.target)) return;
        if (event.target.getAttribute('id') !== 'bpCreateForm') return;
        event.preventDefault();
        void onCreateSubmit(event.target);
    };

    const onSearchInput = (event) => {
        if (!inRoot(event.target)) return;
        const id = event.target.getAttribute('id');
        if (id === 'bpSearch' && detail) renderBills();
        if (id === 'bpTargetSearch' && editing) renderTargetList();
    };

    // 'cancel' của <dialog> không được tin cậy để delegate trên document: gắn riêng cho từng node mới.
    const bindRoot = () => {
        const dialog = $('#bpTargetDialog');
        if (dialog && !dialog.__bpCancelBound) {
            dialog.__bpCancelBound = true;
            dialog.addEventListener('cancel', (event) => {
                event.preventDefault();
                cancelTargets();
            });
        }
    };

    const init = () => {
        const root = document.getElementById(rootId);
        if (!root) return;
        if (root !== mountedRoot) {
            mountedRoot = root;
            periods = [];
            detail = null;
            feeItems = new Map();
            targets = new Map();
            householdOptions = null;
            editing = null;
        }
        if (!bound) {
            document.addEventListener('click', onClick);
            document.addEventListener('submit', onSubmit);
            document.addEventListener('change', onChange);
            document.addEventListener('input', onSearchInput);
            bound = true;
        }
        bindRoot();
        void loadPeriods();
    };

    // Dự phòng: enhancedload không bắt được trường hợp Blazor thay DOM lúc SSR -> interactive.
    const observeRootChanges = () => {
        if (rootObserver) return;
        rootObserver = new MutationObserver(() => {
            const current = document.getElementById(rootId);
            if (current && current !== mountedRoot) init();
        });
        rootObserver.observe(document.documentElement, { childList: true, subtree: true });
    };

    init();
    if (window.Blazor?.addEventListener) {
        window.Blazor.addEventListener('enhancedload', init);
    }
    observeRootChanges();
})();
