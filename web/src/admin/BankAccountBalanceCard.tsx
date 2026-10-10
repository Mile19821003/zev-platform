import { useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { formatMoney } from "../currency.ts";
import { BankAccountBalance, balanceStatus, bankBalanceLine, gapLines, reconciliationRows } from "../bankBalanceUi.ts";

// Bank balance (from statements) next to the fund balance (fund ledger) for one bank account.
export default function BankAccountBalanceCard({ token, zevId, accountId, revision }: { token: string; zevId: string;
  accountId: string; revision: number }) {
  const [balance, setBalance] = useState<BankAccountBalance | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let current = true;
    setBalance(null); setError("");
    callApi<BankAccountBalance>(`/api/zevs/${zevId}/payments/accounts/${accountId}/balance`, {}, token)
      .then((result) => { if (current) setBalance(result); })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, zevId, accountId, revision]);

  if (error) return <p className="error" role="alert">{error}</p>;
  if (!balance) return <p>Učitavanje stanja…</p>;
  const status = balanceStatus(balance);
  const money = (amount: number) => formatMoney(amount, balance.currencyCode);
  const rows = reconciliationRows(balance);
  return <div className="bank-balance" aria-label={`Stanje računa ${balance.accountNumber}`}>
    <p className={`balance-status ${status.tone}`} role="status">{status.label}</p>
    <div className="balance-grid">
      <article className="balance-card">
        <h3>Banka · iz izvoda</h3>
        <strong>{bankBalanceLine(balance)}</strong>
        <dl>
          <dt>Prilivi</dt><dd>{money(balance.bank.credits)}</dd>
          <dt>Odlivi</dt><dd>{money(balance.bank.debits)}</dd>
          <dt>Promet uvezenih izvoda</dt><dd>{money(balance.bank.movement)}</dd>
          {balance.bank.derivedBalance !== null && <><dt>Izračunato (početni saldo + promet)</dt>
            <dd>{money(balance.bank.derivedBalance)}{balance.bank.derivedMatchesReported === false ? " · ne slaže se s izvodom" : ""}</dd></>}
          <dt>Izvoda</dt><dd>{balance.bank.statementCount}</dd>
        </dl>
      </article>
      <article className="balance-card">
        <h3>Fond · knjiga fonda</h3>
        <strong>{money(balance.fund.balance)}</strong>
        <dl>
          <dt>Fond</dt><dd>{balance.fund.fundName}</dd>
          <dt>Uplate s ovog računa</dt><dd>{money(balance.fund.fromThisAccount)}</dd>
          {balance.fund.fromOtherAccounts !== 0 && <><dt>Uplate s drugih računa</dt><dd>{money(balance.fund.fromOtherAccounts)}</dd></>}
          {balance.fund.manualPayments !== 0 && <><dt>Ručne uplate</dt><dd>{money(balance.fund.manualPayments)}</dd></>}
          {balance.fund.expenses !== 0 && <><dt>Rashodi</dt><dd>{money(balance.fund.expenses)}</dd></>}
          {balance.fund.other !== 0 && <><dt>Ostalo</dt><dd>{money(balance.fund.other)}</dd></>}
        </dl>
        {balance.fund.otherCurrencyEntries > 0 && <p>Stavki u drugoj valuti (nisu uračunate): {balance.fund.otherCurrencyEntries}</p>}
      </article>
    </div>
    <h3>Usklađivanje: promet banke − stanje fonda = {money(balance.reconciliation.difference)}</h3>
    {rows.length === 0 ? <p className="empty-inline">Nema razlike između prometa banke i stanja fonda.</p>
      : <div className="table-scroll"><table><tbody>{rows.map((row) => <tr key={row.label}><td>{row.label}</td>
        <td>{row.amount}</td></tr>)}</tbody></table></div>}
    {gapLines(balance).map((line) => <p key={line} className="error">{line}</p>)}
  </div>;
}
