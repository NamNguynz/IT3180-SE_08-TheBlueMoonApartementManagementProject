// Tự động hóa phần chạy trên trình duyệt của T4 bước 5 trong docs/testing/acceptance_session_timeout.md.
// Chạy file JS thật của trang Hồ sơ (wwwroot/js/profile.js) trên DOM giả, API giả (xem dom-stub.mjs).
// Phần server của T4 (hết phiên thì đổi mật khẩu bị từ chối, mật khẩu cũ vẫn dùng được) ở test xUnit Acceptance=T4.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FakeElement, fakeForm, jsonResponse, loadPage } from './dom-stub.mjs';

/** Trang Hồ sơ giả với form Đổi mật khẩu đã điền sẵn như T4 bước 3. */
async function openProfilePage(respond) {
    const fields = {
        current: new FakeElement('input', { name: 'current', value: 'TestPass@01' }),
        next: new FakeElement('input', { name: 'next', value: 'NewPass@02' }),
        confirm: new FakeElement('input', { name: 'confirm', value: 'NewPass@02' })
    };
    const button = new FakeElement('button', { type: 'submit' });
    const form = fakeForm('passwordForm', fields, { submitButton: button });
    const alert = new FakeElement('div', { id: 'pwAlert', hidden: true });

    const page = await loadPage('profile.js', { elements: [form, alert], respond });
    return { ...page, form, button, alert, submit: () => page.dispatch('submit', form) };
}

test('T4 bước 5 (H3, ghi nhận, không đánh Fail): hết phiên khi bấm Đổi mật khẩu thì về /login, không nhớ trang Hồ sơ', async () => {
    const page = await openProfilePage(() => jsonResponse(401, null));

    await page.submit();

    // Đã gửi đúng yêu cầu đổi mật khẩu
    assert.equal(page.requests.length, 1);
    assert.equal(page.requests[0].url, '/api/ho-so/doi-mat-khau');
    assert.deepEqual(JSON.parse(page.requests[0].init.body), { currentPassword: 'TestPass@01', newPassword: 'NewPass@02' });

    // H3: chuyển thẳng về /login, không kèm returnUrl, nên đăng nhập lại sẽ về trang chủ chứ không về /ho-so.
    // Nếu sửa H3 (vd '/login?returnUrl=%2Fho-so') thì cập nhật test này.
    assert.deepEqual(page.location.assigned, ['/login']);
    assert.equal(page.alert.hidden, true, 'Không hiện thông báo lỗi trước khi chuyển trang');
    assert.equal(page.button.disabled, false, 'Nút Đổi mật khẩu được bật lại');
});

test('C12 (G3, issue #69): sai mật khẩu hiện tại lần thứ 5, API trả 423 thì trang hiện câu báo khóa của server', async () => {
    const message = 'Tài khoản tạm bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng đăng nhập lại sau 15 phút.';
    const page = await openProfilePage(() => jsonResponse(423, { code: 'ACCOUNT_LOCKED', message }));

    await page.submit();

    assert.equal(page.alert.hidden, false);
    assert.equal(page.alert.textContent, message);
    assert.equal(page.alert.className, 'alert', 'Thông báo lỗi (đỏ), không phải thông báo thành công');
    assert.deepEqual(page.location.assigned, [], 'Ở lại trang để người dùng đọc thông báo');
    assert.equal(page.button.disabled, false);
});
