import fixture from "./fixtures/owner-finance.json";
import { lastPage, loadOwnerCards, parseOwnerCards, toFinanceView, type OwnerCard } from "../src/finance";

const zevId = fixture.zevId;

describe("owner finance response shape", () => {
  // Guards against the regression where the screen expected `charges`/`payments` arrays.
  it("matches the fields the mobile screen reads", () => {
    const [card] = fixture.cards;
    expect(Object.keys(card)).toEqual(expect.arrayContaining(["personId", "balances", "events", "invoices",
      "page", "pageSize", "totalCount"]));
    expect(card).not.toHaveProperty("charges");
    expect(Object.keys(card.balances[0])).toEqual(expect.arrayContaining(["currencyCode", "totalCharges",
      "totalPayments", "open", "credit", "netBalance"]));
    for (const event of card.events) {
      expect(Object.keys(event)).toEqual(expect.arrayContaining(["date", "kind", "document", "description",
        "currencyCode", "debt", "payment", "chargeId", "paymentId", "period"]));
    }
    expect(Object.keys(card.invoices[0])).toEqual(expect.arrayContaining(["id", "invoiceNumber", "period",
      "dueDate", "currency", "effectiveAmount", "open", "documentStatus", "paymentStatus"]));
  });

  it("rejects the old or an unexpected shape with a readable error", () => {
    expect(() => parseOwnerCards({ items: [] })).toThrow("Neočekivan odgovor finansijske kartice.");
    expect(() => parseOwnerCards([{ personId: "p", balance: 10, charges: [], payments: [] }]))
      .toThrow("Neočekivan odgovor finansijske kartice.");
    expect(parseOwnerCards(fixture.cards)).toHaveLength(1);
  });
});

describe("finance view", () => {
  const view = toFinanceView(zevId, parseOwnerCards(fixture.cards));

  it("shows the balance per currency in KM", () => {
    expect(view.balances).toEqual([{ key: "BAM", currencyCode: "BAM", title: "Za uplatu: 10,00 KM",
      detail: "Zaduženo 25,00 KM · uplaćeno 15,00 KM" }]);
  });

  it("shows the corrected, partially paid invoice with a slip for its open charge", () => {
    expect(view.invoices).toHaveLength(1);
    const [invoice] = view.invoices;
    expect(invoice.title).toBe("INV-2026-000001 · 10/2026");
    expect(invoice.detail).toBe("Iznos 25,00 KM · otvoreno 10,00 KM · rok 30.10.2026.");
    expect(invoice.status).toBe("Djelimično plaćeno · Korigovan");
    const chargeId = fixture.cards[0].events.find((event) => event.kind === "Invoice")!.chargeId;
    expect(invoice.slips).toEqual([{ chargeId, label: "Uplatnica (PDF)",
      path: `/api/zevs/${zevId}/charges/${chargeId}/payment-slip.pdf` }]);
  });

  it("lists payments newest first and ignores allocations and corrections", () => {
    expect(view.payments.map((payment) => payment.text)).toEqual(["10.10.2026. · 5,00 KM", "10.10.2026. · 10,00 KM"]);
    expect(view.legacyCharges).toEqual([]);
    expect(view.partial).toBe(false);
  });

  it("offers no slip for paid or cancelled invoices", () => {
    const card = parseOwnerCards(fixture.cards)[0];
    const paid = { ...card, invoices: [{ ...card.invoices[0], open: 0, paymentStatus: "Paid" }] };
    const cancelled = { ...card, invoices: [{ ...card.invoices[0], open: 30, documentStatus: "Cancelled" }] };
    expect(toFinanceView(zevId, [paid]).invoices[0]).toMatchObject({ open: false, slips: [], status: "Plaćeno · Korigovan" });
    expect(toFinanceView(zevId, [cancelled]).invoices[0]).toMatchObject({ open: false, slips: [], status: "Storniran" });
  });

  it("labels each slip by line when an invoice has several charges", () => {
    const card = parseOwnerCards(fixture.cards)[0];
    const second = { ...card.events[0], chargeId: "second-charge", description: "Rezervni fond" };
    const view = toFinanceView(zevId, [{ ...card, events: [...card.events, second] }]);
    expect(view.invoices[0].slips.map((slip) => slip.label))
      .toEqual(["Uplatnica · Održavanje po m²", "Uplatnica · Rezervni fond"]);
  });

  it("adds balances of several cards per currency and reports credit", () => {
    const base = parseOwnerCards(fixture.cards)[0];
    const other: OwnerCard = { ...base, personId: "other", invoices: [], events: [],
      balances: [{ currencyCode: "BAM", totalCharges: 0, totalPayments: 20, open: 0, credit: 20, netBalance: -20 },
        { currencyCode: "EUR", totalCharges: 5, totalPayments: 10, open: 0, credit: 5, netBalance: -5 }] };
    expect(toFinanceView(zevId, [base, other]).balances).toEqual([
      { key: "BAM", currencyCode: "BAM", title: "Za uplatu: 10,00 KM",
        detail: "Zaduženo 25,00 KM · uplaćeno 35,00 KM · pretplata 20,00 KM" },
      { key: "EUR", currencyCode: "EUR", title: "Pretplata: 5,00 EUR", detail: "Zaduženo 5,00 EUR · uplaćeno 10,00 EUR" },
    ]);
  });
});

describe("loading cards", () => {
  it("reads the last page when the ledger has more entries than one page", async () => {
    const card = parseOwnerCards(fixture.cards)[0];
    const paths: string[] = [];
    const cards = await loadOwnerCards(async (path) => {
      paths.push(path);
      return [{ ...card, totalCount: 250, page: path.includes("page=3") ? 3 : 1 }];
    }, zevId);
    expect(paths).toEqual([`/api/me/finance/${zevId}?page=1&pageSize=100`, `/api/me/finance/${zevId}?page=3&pageSize=100`]);
    expect(cards[0].page).toBe(3);
    expect(toFinanceView(zevId, cards).partial).toBe(true);
    expect(lastPage([])).toBe(1);
  });

  it("reads one page for a short ledger", async () => {
    const get = jest.fn(async () => fixture.cards);
    await loadOwnerCards(get, zevId);
    expect(get).toHaveBeenCalledTimes(1);
  });
});
