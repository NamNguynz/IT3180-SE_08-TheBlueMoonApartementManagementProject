// Tự động hóa phần chạy trên trình duyệt của docs/testing/acceptance_audit_log.md:
// D3 bước 5, D5 bước 2, D6 (nút Trước/Sau, dòng "Trang X / Y"), D8, D9, D11 bước 3.
// Chạy file JS thật của trang (wwwroot/js/audit-log.js) trên DOM giả, API giả (xem dom-stub.mjs).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FakeElement, fakeForm, jsonResponse, loadPage } from './dom-stub.mjs';

const PAGE_SIZE = 20;

/** Dữ liệu giả cho một trang nhật ký: mỗi dòng một lần đăng nhập thất bại. */
const fakeItems = (page, count) => Array.from({ length: count }, (_, i) => ({
    id: page * 100 + i,
    occurredAt: '2026-11-25T07:05:09.123Z',
    username: `user_${page}_${i}`,
    action: 'LoginFailed',
    target: null,
    detail: 'Sai mật khẩu, lần 1'
}));

/**
 * Trang Nhật ký giả với đúng các id và ô lọc của AuditLogPage.razor.
 * API giả trả `total` bản ghi, chia trang theo tham số page/pageSize trong URL.
 */
async function openAuditPage({ total = 45, items } = {}) {
    const fields = {
        from: new FakeElement('input', { name: 'from', value: '' }),
        to: new FakeElement('input', { name: 'to', value: '' }),
        action: new FakeElement('select', {
            name: 'action',
            value: '',
            // Nhãn lấy từ option do AuditActionLabels.cs sinh ra; có khoảng trắng thừa để kiểm việc cắt
            options: [
                { value: '', textContent: 'Tất cả' },
                { value: 'LoginFailed', textContent: '  Đăng nhập thất bại  ' },
                { value: 'ChangeRate', textContent: 'Điều chỉnh giá khoản thu' }
            ]
        }),
        username: new FakeElement('input', { name: 'username', value: '' })
    };
    const form = fakeForm('auditFilter', fields);
    const reset = new FakeElement('button', { id: 'auditReset' });
    reset.parentElement = form;

    const ids = ['auditAlert', 'auditLoading', 'auditEmpty', 'auditTable', 'auditBody', 'auditPager',
        'auditPrev', 'auditPageInfo', 'auditNext'];
    const elements = [form, reset, new FakeElement('select', { id: 'auditPageSize', value: String(PAGE_SIZE) }),
        ...ids.map((id) => new FakeElement('div', { id }))];
    const byId = (id) => elements.find((e) => e.id === id);

    const page = await loadPage('audit-log.js', {
        elements,
        respond: (url) => {
            const query = new URL(url, 'https://localhost').searchParams;
            const p = Number(query.get('page'));
            const size = Number(query.get('pageSize'));
            const count = Math.max(0, Math.min(size, total - (p - 1) * size));
            return jsonResponse(200, { items: items ?? fakeItems(p, count), total, page: p, pageSize: size });
        }
    });

    const query = () => new URL(page.lastUrl(), 'https://localhost').searchParams;
    const click = (id) => page.dispatch('click', byId(id));
    const submit = () => page.dispatch('submit', form);
    return { ...page, fields, form, byId, query, click, submit };
}

test('D3 bước 5: tên tài khoản có khoảng trắng hai đầu được cắt trước khi gửi, kết quả như khi gõ không có khoảng trắng', async () => {
    const page = await openAuditPage();

    page.fields.username.value = 'cudan_nk';
    await page.submit();
    const plain = page.lastUrl();

    page.fields.username.value = '   cudan_nk   ';
    const event = await page.submit();

    assert.equal(page.query().get('username'), 'cudan_nk');
    assert.equal(page.lastUrl(), plain);
    assert.equal(event.defaultPrevented, true, 'Bấm Lọc không được tải lại cả trang');
});

test('D5: lọc kết hợp gửi đủ 4 điều kiện; ngày Việt Nam đổi sang giờ UTC, "Đến ngày" tính trọn ngày', async () => {
    const page = await openAuditPage();

    page.fields.action.value = 'LoginFailed';
    page.fields.username.value = 'cudan_nk';
    page.fields.from.value = '2026-11-25';
    page.fields.to.value = '2026-11-25';
    await page.submit();

    const q = page.query();
    assert.equal(q.get('action'), 'LoginFailed');
    assert.equal(q.get('username'), 'cudan_nk');
    assert.equal(q.get('from'), '2026-11-24T17:00:00.000Z');
    assert.equal(q.get('to'), '2026-11-25T17:00:00.000Z');
    assert.equal(q.get('page'), '1');
});

test('D5 bước 2: Xóa lọc đưa mọi ô lọc về trống và tải lại Trang 1 không còn điều kiện lọc', async () => {
    const page = await openAuditPage({ total: 45 });
    page.fields.action.value = 'LoginFailed';
    page.fields.username.value = 'cudan_nk';
    page.fields.from.value = '2026-11-25';
    page.fields.to.value = '2026-11-25';
    await page.submit();
    await page.click('auditNext');                       // đang ở trang 2 của kết quả lọc
    assert.equal(page.query().get('page'), '2');

    await page.click('auditReset');

    assert.equal(page.form.resetCount, 1);
    for (const name of ['action', 'username', 'from', 'to'])
        assert.equal(page.fields[name].value, '', `Ô ${name} phải về trống`);
    assert.equal(page.lastUrl(), `/api/nhat-ky?page=1&pageSize=${PAGE_SIZE}`);
    assert.equal(page.byId('auditPageInfo').textContent, 'Trang 1 / 3 (45 bản ghi)');
});

test('D6: phân trang 45 bản ghi: dòng "Trang X / Y", nút Trước/Sau bị tắt đúng lúc, lọc thì về Trang 1', async () => {
    const page = await openAuditPage({ total: 45 });
    const info = () => page.byId('auditPageInfo').textContent;
    const prev = page.byId('auditPrev');
    const next = page.byId('auditNext');
    const rows = () => page.byId('auditBody').children.length;

    // Bước 1: mở trang
    assert.equal(info(), 'Trang 1 / 3 (45 bản ghi)');
    assert.equal(rows(), 20);
    assert.equal(prev.disabled, true);
    assert.equal(next.disabled, false);

    // Bước 3: Sau → sang trang 2
    await page.click('auditNext');
    assert.equal(page.query().get('page'), '2');
    assert.equal(info(), 'Trang 2 / 3 (45 bản ghi)');
    assert.equal(prev.disabled, false);

    // Bước 4: tới trang cuối, còn 45 − 20 × 2 = 5 dòng, nút Sau bị tắt; bấm nữa thì không gọi API
    await page.click('auditNext');
    assert.equal(info(), 'Trang 3 / 3 (45 bản ghi)');
    assert.equal(rows(), 5);
    assert.equal(next.disabled, true);
    const before = page.requests.length;
    await page.click('auditNext');
    assert.equal(page.requests.length, before, 'Nút Sau đang tắt thì không được tải thêm');

    // Bước 5: ← Trước quay lại trang 2
    await page.click('auditPrev');
    assert.equal(page.query().get('page'), '2');
    assert.equal(info(), 'Trang 2 / 3 (45 bản ghi)');

    // Bước 6: đang ở trang 2 mà lọc thì về Trang 1 của kết quả lọc
    page.fields.action.value = 'LoginFailed';
    await page.submit();
    assert.equal(page.query().get('page'), '1');
    assert.equal(page.query().get('action'), 'LoginFailed');
});

test('D6: không có bản ghi nào thì ẩn bảng và thanh phân trang, hiện dòng "không có nhật ký"', async () => {
    const page = await openAuditPage({ total: 0 });

    assert.equal(page.byId('auditTable').hidden, true);
    assert.equal(page.byId('auditPager').hidden, true);
    assert.equal(page.byId('auditEmpty').hidden, false);
});

test('D8: tên đăng nhập và chi tiết có mã HTML được hiện nguyên văn, không tạo thẻ HTML nào', async () => {
    const evil = '<img src=x onerror=alert(1)>';
    const page = await openAuditPage({
        total: 1,
        items: [{
            id: 1, occurredAt: '2026-11-25T07:05:09.123Z', username: evil,
            action: 'LoginFailed', target: null, detail: '<script>alert(1)</script>'
        }]
    });

    const [row] = page.byId('auditBody').children;
    const cells = row.children;
    assert.equal(cells[1].textContent, evil);
    assert.equal(cells[4].textContent, '<script>alert(1)</script>');
    // Trang chỉ tạo tr, td, span; ghi innerHTML sẽ làm DOM giả ném lỗi
    assert.deepEqual([...new Set(page.created.map((e) => e.tagName))].sort(), ['SPAN', 'TD', 'TR']);

    // Bước 4: lọc theo "<img" thì gửi đúng chuỗi đó (đã mã hóa trong URL) lên API
    page.fields.username.value = '<img';
    await page.submit();
    assert.equal(page.query().get('username'), '<img');
    assert.match(page.lastUrl(), /username=%3Cimg/);
});

test('D9: thời gian hiện theo giờ Việt Nam, đủ ngày tháng năm giờ phút giây và 3 số mili giây', async () => {
    const page = await openAuditPage({
        total: 2,
        items: [
            { id: 1, occurredAt: '2026-11-25T07:05:09.123Z', username: 'a', action: 'LoginFailed' },
            // 17:30 UTC là 00:30 ngày hôm sau ở Việt Nam
            { id: 2, occurredAt: '2026-11-25T17:30:00.007Z', username: 'b', action: 'LoginFailed' }
        ]
    });

    const times = page.byId('auditBody').children.map((r) => r.children[0].textContent);
    assert.match(times[0], /14:05:09/);
    assert.match(times[0], /25\/11\/2026/);
    assert.match(times[0], /\.123$/);
    assert.match(times[1], /00:30:00/);
    assert.match(times[1], /26\/11\/2026/);
    assert.match(times[1], /\.007$/);
});

test('J7 (ghi nhận, không đánh Fail): kiểu vi-VN đặt giờ trước ngày nên mili giây dính sau năm', async () => {
    const page = await openAuditPage({
        total: 1,
        items: [{ id: 1, occurredAt: '2026-11-25T07:05:09.123Z', username: 'a', action: 'LoginFailed' }]
    });

    // Ghi nhận hành vi hiện tại. Nếu sửa J7 (vd "25/11/2026 14:05:09.123") thì cập nhật test này.
    assert.equal(page.byId('auditBody').children[0].children[0].textContent, '14:05:09 25/11/2026.123');
});

test('D11 bước 3: cột Hành động hiện nhãn tiếng Việt lấy từ ô lọc; loại chưa có nhãn thì hiện tên gốc', async () => {
    const page = await openAuditPage({
        total: 2,
        items: [
            { id: 1, occurredAt: '2026-11-25T07:05:09.123Z', username: 'admin', action: 'ChangeRate', target: '7' },
            { id: 2, occurredAt: '2026-11-25T07:05:09.123Z', username: 'admin', action: 'UnknownAction', target: '8' }
        ]
    });

    const [first, second] = page.byId('auditBody').children;
    assert.equal(first.children[2].children[0].textContent, 'Điều chỉnh giá khoản thu');
    assert.equal(first.children[3].textContent, '7');
    assert.equal(second.children[2].children[0].textContent, 'UnknownAction');
    assert.equal(second.children[4].textContent, '—', 'Thiếu chi tiết thì hiện dấu gạch');
});

test('Hết phiên khi đang xem Nhật ký (API trả 401) thì chuyển về trang đăng nhập, không hiện lỗi', async () => {
    const fields = {
        from: new FakeElement('input', { name: 'from' }), to: new FakeElement('input', { name: 'to' }),
        action: new FakeElement('select', { name: 'action', options: [] }),
        username: new FakeElement('input', { name: 'username' })
    };
    const ids = ['auditAlert', 'auditLoading', 'auditEmpty', 'auditTable', 'auditBody', 'auditPager',
        'auditPrev', 'auditPageInfo', 'auditNext', 'auditPageSize'];
    const elements = [fakeForm('auditFilter', fields), ...ids.map((id) => new FakeElement('div', { id }))];

    const page = await loadPage('audit-log.js', { elements, respond: () => jsonResponse(401, null) });

    assert.deepEqual(page.location.assigned, ['/login']);
    assert.equal(elements.find((e) => e.id === 'auditAlert').textContent, '');
});
