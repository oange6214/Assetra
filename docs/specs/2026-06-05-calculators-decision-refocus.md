# 理財試算 → 決策試算 Refocus (v2)

- **日期**：2026-06-05
- **狀態**：設計核准，實作中
- **取代**：部分 `docs/specs/2026-06-03-financial-calculators.md`（4 計算機 → 2）

## 背景

原 4 計算機（貸款、拿鐵因子、72 法則、租vs買）上線後，檢視定位發現重疊：

- **拿鐵因子**（年金 FV）數學＝財務目標 `GoalPlanningService`，僅動機/教育包裝。
- **72 法則**（翻倍近似）與 FIRE / Monte Carlo 的真實複利推估重疊，屬教育玩具。
- **貸款計算**：既有「負債」功能只在存檔真實貸款後算攤還，無法試算「未承諾的假設貸款」→ 計算機補「承諾前 what-if」缺口，不重疊。
- **租 vs 買**：全 app 唯一比較租/買長期淨成本者，無重疊。

## 決策

理財試算重新定位為「**承諾前的決策試算**」，只保留兩個填補真實缺口的工具：

1. 貸款計算
2. 租 vs 買

**完全移除**拿鐵因子、72 法則。為留存兩者各加一行情境副標，讓使用者一眼知道「這幫我決定什麼」。

## 範圍（外科手術式）

1. 完全移除 RuleOf72 與 LatteFactor：Core model、Application calculator + 測試、子 VM、子 View(.xaml/.cs)、父 VM 屬性與建構參數、TabControl 分頁、DI 註冊、VM 測試類、雙語 `Calc.Rule72.*`/`Calc.Latte.*`/`Calc.Tab.{Rule72,Latte}` keys。
2. 留存兩分頁各加情境副標（`Calc.Loan.Subtitle`、`Calc.RentVsBuy.Subtitle`，雙語）。

## 不做

- 不重排版面、不加新計算機、不動 FIRE/目標/Monte Carlo/負債、側欄名「理財試算」不變、留存兩者的 code 識別字不變。
