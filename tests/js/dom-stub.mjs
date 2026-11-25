// DOM giả tối thiểu để chạy file JS thật của trang (wwwroot/js) trong Node, không cần trình duyệt hay gói ngoài.
// Chạy: node --test tests/js/*.test.mjs
//
// Giới hạn: đây không phải trình duyệt. Test kiểm logic của file JS (gửi API gì, ghi chữ gì vào ô nào,
// nút nào bị tắt, chuyển trang đi đâu), không kiểm màu sắc, bố cục, CSS.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const JS_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)),
    '../../BlueMoonApartementManagementConsole/wwwroot/js');

/** Phần tử giả. Ghi bằng innerHTML/outerHTML/insertAdjacentHTML sẽ ném lỗi: trang phải ghi chữ bằng textContent. */
export class FakeElement {
    constructor(tag = 'div', props = {}) {
        this.tagName = tag.toUpperCase();
        this.id = '';
        this.className = '';
        this.textContent = '';
        this.value = '';
        this.hidden = false;
        this.disabled = false;
        this.dataset = {};
        this.children = [];
        this.parentElement = null;
        Object.assign(this, props);
    }

    appendChild(child) {
        child.parentElement = this;
        this.children.push(child);
        return child;
    }

    replaceChildren(...children) {
        children.forEach((c) => { c.parentElement = this; });
        this.children = children;
    }

    /** Chỉ hỗ trợ bộ chọn theo id, vd '#auditPrev, #auditNext'. */
    closest(selector) {
        const ids = selector.split(',').map((s) => s.trim().replace(/^#/, ''));
        for (let node = this; node; node = node.parentElement) {
            if (ids.includes(node.id)) return node;
        }
        return null;
    }

    set innerHTML(html) { throw new Error(`Không được ghi innerHTML: ${html}`); }
    get innerHTML() { throw new Error('Không được đọc innerHTML'); }
    set outerHTML(html) { throw new Error(`Không được ghi outerHTML: ${html}`); }
    insertAdjacentHTML(_, html) { throw new Error(`Không được dùng insertAdjacentHTML: ${html}`); }
}

/** Form giả: elements.namedItem(tên) trả ô nhập, reset() đưa mọi ô về giá trị ban đầu. */
export function fakeForm(id, fields, { submitButton } = {}) {
    const defaults = Object.fromEntries(Object.entries(fields).map(([name, f]) => [name, f.value]));
    const form = new FakeElement('form', { id });
    form.resetCount = 0;
    form.elements = { namedItem: (name) => fields[name] ?? null };
    form.reset = () => {
        form.resetCount++;
        for (const [name, f] of Object.entries(fields)) f.value = defaults[name];
    };
    form.querySelector = (selector) => (selector === 'button[type="submit"]' ? submitButton ?? null : null);
    Object.values(fields).forEach((f) => { f.parentElement = form; });
    if (submitButton) submitButton.parentElement = form;
    return form;
}

/** Phản hồi fetch giả. */
export const jsonResponse = (status, body) => ({
    status,
    ok: status >= 200 && status < 300,
    json: async () => body
});

/** Chờ các promise đang dở (fetch, res.json, render) chạy xong. */
export const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

/**
 * Nạp một file trong wwwroot/js vào trang giả.
 * elements: các phần tử có id trên trang. respond(url, init): trả phản hồi cho mỗi lần fetch.
 */
export async function loadPage(scriptName, { elements, respond }) {
    const byId = new Map(elements.map((e) => [e.id, e]));
    const listeners = {};
    const requests = [];
    const created = [];

    const document = {
        getElementById: (id) => byId.get(id) ?? null,
        createElement: (tag) => {
            const e = new FakeElement(tag);
            created.push(e);
            return e;
        },
        addEventListener: (type, fn) => { (listeners[type] ??= []).push(fn); }
    };
    const location = {
        assigned: [],
        assign(url) { this.assigned.push(url); }
    };
    const fetch = async (url, init) => {
        requests.push({ url: String(url), init });
        return respond(String(url), init);
    };

    const context = vm.createContext({
        document, window: {}, location, fetch, URLSearchParams, setTimeout, console
    });
    const source = readFileSync(path.join(JS_DIR, scriptName), 'utf8');
    vm.runInContext(source, context, { filename: scriptName });
    await flush();

    /** Phát một sự kiện như trình duyệt (click, submit, change) rồi chờ trang xử lý xong. */
    const dispatch = async (type, target) => {
        const event = { type, target, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
        for (const fn of listeners[type] ?? []) await fn(event);
        await flush();
        return event;
    };

    return { requests, created, location, dispatch, lastUrl: () => requests.at(-1)?.url };
}
