// Trang Ghi nhận thu tiền (PBI-016). Quy tắc ghi nhận nằm ở PaymentService;
// file này chỉ hiện tình hình thu theo kỳ, hộp thoại ghi nhận (có bước xác nhận) và gọi API api/thu-tien.
(() => {
    const rootId = 'payments';
    let periods = [];
    let collection = null;   // tình hình thu của kỳ đang chọn
    let bill = null;          // khoản phải thu của hộ đang mở hộp thoại
    let pendingBody = null;   // nội dung đã nhập, chờ Kế toán xác nhận
    let initializedRoot;

    const $ = (selector) => document.querySelector(`#${rootId} ${selector}`);
    const formValue = (form, name) => form.elements.namedItem(name).value;

    const pad = (n) => String(n).padStart(2, '0');
    const formatMoney = (value) => Number(value).toLocaleString('vi-VN');
    // Thời điểm (lưu UTC): hiển thị theo giờ máy người dùng
    const formatTime = (value) => value
        ? new Date(value).toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
        : '';
    const formatDate = (value) => {
        if (!value) return '';
        const [year, month, day] = value.slice(0, 10).split('-');
        return `${day}/${month}/${year}`;
    };
    // Giá trị cho ô datetime-local theo giờ máy: yyyy-MM-ddTHH:mm
    const localDateTime = (date) =>
        `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    const periodName = (period) => `Tháng ${pad(period.month)}/${period.year}`;

    // Nhãn lấy từ datalist (trang sinh ra từ PaymentLabels.cs)
    const labels = (selector) => Object.fromEntries([...$(selector).options].map((o) => [o.value, o.textContent.trim()]));
    const methodLabel = (method) => labels('#pmMethodLabels')[method] ?? method;
    const statusLabel = (status) => labels('#pmStatusLabels')[status] ?? status;
    const statusBadgeClass = (status) => ({
        ChuaNop: 'badge badge-muted',
        NopMotPhan: 'badge badge-warn',
        DaNopDu: 'badge badge-ok'
    })[status] ?? 'badge';

    const showAlert = (message, success = false, selector = '#pmAlert') => {
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

    // ---- Kỳ thu và tình hình thu ----

    const loadPeriods = async () => {
        try {
            const response = await api('/api/ky-thu');
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không tải được danh sách kỳ thu.'));
                return;
            }
            periods = response.data ?? [];
            $('#pmPeriod').replaceChildren(...periods.map((period) => {
                const option = document.createElement('option');
                option.value = period.id;
                option.textContent = `${periodName(period)} · hạn nộp ${formatDate(period.dueDate)}`;
                return option;
            }));
            $('#pmNoPeriod').hidden = periods.length > 0;
            $('#pmPeriodView').hidden = periods.length === 0;
            if (periods.length > 0) await loadCollection(); // kỳ mới nhất được chọn sẵn
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    // Tải lỗi thì ẩn bảng, để không còn hiện số liệu của kỳ cũ dưới ô đã chọn kỳ khác (dễ ghi nhầm kỳ)
    const loadCollection = async () => {
        const periodId = $('#pmPeriod').value;
        if (!periodId) return;
        try {
            const response = await api(`/api/thu-tien/ky-thu/${periodId}`);
            if (response.status === 401) return;
            if (!response.ok) {
                $('#pmPeriodView').hidden = true;
                showAlert(messageFor(response, 'Không tải được tình hình thu của kỳ.'));
                return;
            }
            collection = response.data;
            $('#pmPeriodView').hidden = false;
            renderCollection();
        } catch {
            $('#pmPeriodView').hidden = true;
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    const renderCollection = () => {
        $('#pmDue').textContent = `${formatMoney(collection.amountDue)} đ`;
        $('#pmPaid').textContent = `${formatMoney(collection.amountPaid)} đ`;
        $('#pmRemaining').textContent = `${formatMoney(collection.remaining)} đ`;

        const status = $('#pmStatus').value;
        const query = $('#pmSearch').value.trim().toLocaleLowerCase('vi');
        const households = collection.households.filter((household) =>
            (!status || household.status === status) &&
            (!query ||
                household.apartmentCode.toLocaleLowerCase('vi').includes(query) ||
                household.building.toLocaleLowerCase('vi').includes(query) ||
                household.householdNumber.toLocaleLowerCase('vi').includes(query)));

        $('#pmHouseholds').replaceChildren(...households.map((household) => {
            const row = document.createElement('tr');
            const badge = document.createElement('span');
            badge.className = statusBadgeClass(household.status);
            badge.textContent = statusLabel(household.status);
            const statusCell = document.createElement('td');
            statusCell.append(badge);

            const actions = document.createElement('td');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn-link';
            button.dataset.action = 'open';
            button.dataset.household = household.householdId;
            button.textContent = household.status === 'DaNopDu' ? 'Xem biên nhận' : 'Ghi nhận thu';
            actions.append(button);

            row.append(
                cell(household.apartmentCode),
                cell(household.building),
                cell(household.householdNumber),
                cell(formatMoney(household.amountDue), 'num'),
                cell(formatMoney(household.amountPaid), 'num'),
                cell(formatMoney(household.remaining), 'num'),
                statusCell,
                actions);
            return row;
        }));
        $('#pmHouseholdsEmpty').hidden = households.length > 0;
    };

    // ---- Hộp thoại ghi nhận ----

    const openBill = async (householdId) => {
        try {
            const response = await api(`/api/thu-tien/ky-thu/${collection.billingPeriodId}/ho/${householdId}`);
            if (response.status === 401) return;
            if (!response.ok) {
                showAlert(messageFor(response, 'Không tải được khoản phải thu của hộ.'));
                return;
            }
            $('#pmReceipt').hidden = true;
            $('#pmDialogAlert').hidden = true;
            showBill(response.data);
            resetForm();
            $('#pmDialog').hidden = false;
            (bill.remaining > 0 ? $('#pmForm').elements.namedItem('amount') : $('.pm-dialog-close')).focus();
        } catch {
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.');
        }
    };

    const showBill = (data) => {
        bill = data;
        $('#pmDialogPeriod').textContent = `Kỳ thu ${periodName(bill).toLocaleLowerCase('vi')} · hạn nộp ${formatDate(bill.dueDate)}`;
        $('#pmDialogTitle').textContent = `Hộ ${bill.householdNumber} · Căn ${bill.apartmentCode} (${bill.building})`;

        $('#pmLines').replaceChildren(...bill.lines.map((line) => {
            const row = document.createElement('tr');
            row.append(cell(`${line.code} – ${line.name}`), cell(formatMoney(line.amount), 'num'));
            return row;
        }));
        $('#pmBillDue').textContent = `${formatMoney(bill.amountDue)} đ`;
        $('#pmBillPaid').textContent = `${formatMoney(bill.amountPaid)} đ`;
        $('#pmBillRemaining').textContent = `${formatMoney(bill.remaining)} đ`;

        $('#pmPayments').replaceChildren(...bill.payments.map((payment) => {
            const row = document.createElement('tr');
            row.append(
                cell(payment.receiptNumber),
                cell(formatTime(payment.paidAt)),
                cell(formatMoney(payment.amount), 'num'),
                cell(methodLabel(payment.method)),
                cell(payment.reference ?? ''),
                cell(payment.payerName ?? ''),
                cell(`${payment.recordedBy} · ${formatTime(payment.recordedAt)}`));
            return row;
        }));
        $('#pmPaymentsEmpty').hidden = bill.payments.length > 0;

        // Đã nộp đủ thì không còn form ghi nhận
        $('#pmForm').hidden = bill.remaining <= 0;
        $('#pmPaidInFull').hidden = bill.remaining > 0;
    };

    // Chuyển khoản thì bắt buộc mã giao dịch / nội dung để đối chiếu (server cũng kiểm)
    const toggleReference = (form) => {
        const isTransfer = formValue(form, 'method') === 'ChuyenKhoan';
        form.elements.namedItem('reference').required = isTransfer;
    };

    const resetForm = () => {
        const form = $('#pmForm');
        form.reset();
        const now = new Date();
        const amount = form.elements.namedItem('amount');
        amount.max = bill.remaining;
        amount.value = bill.remaining; // thường cư dân nộp đủ số còn thiếu
        form.elements.namedItem('paidAt').value = localDateTime(now);
        form.elements.namedItem('paidAt').max = localDateTime(now);
        toggleReference(form);
        setConfirming(false);
    };

    // Khi đang xác nhận thì khóa các ô, để nội dung lưu đúng là nội dung đã xem lại
    const setConfirming = (confirming) => {
        const form = $('#pmForm');
        for (const field of form.querySelectorAll('input, select')) field.disabled = confirming;
        $('#pmConfirm').hidden = !confirming;
        $('#pmFormActions').hidden = confirming;
        if (!confirming) pendingBody = null;
    };

    const onFormSubmit = (form) => {
        // Giờ đã trôi từ lúc mở hộp thoại: cập nhật giới hạn "không sau hiện tại" trước khi kiểm (server cũng kiểm)
        form.elements.namedItem('paidAt').max = localDateTime(new Date());
        if (!form.reportValidity()) return;
        const amount = Number(formValue(form, 'amount'));
        const method = formValue(form, 'method');
        pendingBody = {
            billingPeriodId: bill.billingPeriodId,
            householdId: bill.householdId,
            amount,
            paidAt: new Date(formValue(form, 'paidAt')).toISOString(),
            method,
            reference: formValue(form, 'reference').trim() || null,
            payerName: formValue(form, 'payerName').trim() || null,
            note: formValue(form, 'note').trim() || null
        };

        const after = bill.remaining - amount;
        $('#pmConfirmText').textContent =
            `Ghi nhận ${formatMoney(amount)} đ (${methodLabel(method)}) cho hộ ${bill.householdNumber}, ` +
            `nhận lúc ${formatTime(pendingBody.paidAt)}. ` +
            (after > 0
                ? `Sau lần này hộ còn thiếu ${formatMoney(after)} đ (nộp một phần).`
                : 'Sau lần này hộ đã nộp đủ kỳ thu.') +
            ' Lần nộp đã ghi không sửa, không xóa được.';
        setConfirming(true);
        $('#pmConfirm').querySelector('[data-action="confirm"]').focus();
    };

    const onConfirm = async (button) => {
        if (!pendingBody) return;
        button.disabled = true;
        try {
            const response = await api('/api/thu-tien', { method: 'POST', body: pendingBody });
            if (response.status === 401) return;
            if (!response.ok) {
                setConfirming(false);
                // Dữ liệu có thể vừa đổi (người khác ghi): tải lại để thấy số còn thiếu mới.
                // Tải lại trước rồi mới hiện lỗi, vì openBill ẩn khung thông báo của hộp thoại.
                if (response.status === 409) await openBill(bill.householdId);
                showAlert(messageFor(response, 'Không ghi nhận được lần nộp.'), false, '#pmDialogAlert');
                return;
            }

            const { payment, bill: updated } = response.data;
            showBill(updated);
            resetForm();
            $('#pmDialogAlert').hidden = true;
            const receipt = $('#pmReceipt');
            receipt.textContent =
                `Đã ghi nhận. Biên nhận ${payment.receiptNumber}: ${formatMoney(payment.amount)} đ, ` +
                `${methodLabel(payment.method)}, nhận lúc ${formatTime(payment.paidAt)}. ` +
                (updated.remaining > 0 ? `Còn thiếu ${formatMoney(updated.remaining)} đ.` : 'Hộ đã nộp đủ kỳ thu.');
            receipt.hidden = false;
            await loadCollection();
        } catch {
            setConfirming(false);
            showAlert('Không thể kết nối máy chủ. Vui lòng thử lại.', false, '#pmDialogAlert');
        } finally {
            button.disabled = false;
        }
    };

    const onClick = async (event) => {
        const button = event.target.closest('[data-action]');
        if (!button || !document.getElementById(rootId).contains(button)) return;

        switch (button.dataset.action) {
            case 'open':
                await openBill(Number(button.dataset.household));
                break;
            case 'confirm':
                await onConfirm(button);
                break;
            case 'edit':
                setConfirming(false);
                break;
            case 'close':
                $('#pmDialog').hidden = true;
                break;
        }
    };

    const init = () => {
        const root = document.getElementById(rootId);
        if (!root || root === initializedRoot) return;
        initializedRoot = root;
        periods = [];
        collection = null;
        bill = null;
        pendingBody = null;
        root.addEventListener('click', onClick);
        root.addEventListener('submit', (event) => {
            if (event.target.getAttribute('id') !== 'pmForm') return;
            event.preventDefault();
            onFormSubmit(event.target);
        });
        root.addEventListener('change', (event) => {
            if (event.target.getAttribute('id') === 'pmPeriod') void loadCollection();
            if (event.target.getAttribute('id') === 'pmStatus') collection && renderCollection();
            if (event.target.form?.getAttribute('id') === 'pmForm' && event.target.name === 'method') toggleReference(event.target.form);
        });
        $('#pmSearch').addEventListener('input', () => collection && renderCollection());
        void loadPeriods();
    };

    init();
    if (window.Blazor?.addEventListener) {
        Blazor.addEventListener('enhancedload', init);
    }
})();
