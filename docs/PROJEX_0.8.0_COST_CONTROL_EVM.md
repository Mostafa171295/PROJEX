# PROJEX 0.8.0-preview.1 — Cost Control & EVM

**حالة التحقق:** كُتب بدون .NET SDK أو PostgreSQL. لم يُبنَ ولم يُشغَّل. شغّل `dotnet build && dotnet test` أولًا (الاختبارات تحتاج Docker).

## المضاف
- `Projex.Cost.Engine`: `EvmCalculator` (3 طرق EAC)، `PlannedValueCalculator`، `CashFlowCurveBuilder`.
- Domain: `CostCode`, `BoqItem`, `BudgetVersion/BudgetLine`, `CostTransaction`, `PerformanceSnapshot`.
- Migrations: `007_costing_progress.sql` (جداول + RLS + triggers)، `008_baseline_immutability.sql`.
- API تحت `/api/v1/projects/{id}/costing`: `cost-codes`, `boq`, `budgets` (+`lines`, `submit`, `return`, `approve`), `transactions` (+`submit/approve/reject/reverse`), `summary`, `evm`, `evm/snapshots`, `cash-flow`.
- صلاحيات: `ReadCost` (PM, CostControl, Viewer)، `ManageCost` (PM, CostControl)، `ApproveCost` (PM) + CompanyAdmin.

## القواعد
- عملة واحدة لكل مشروع. الأرقام decimal(20,4) بتقريب banker's.
- المعاملة المعتمدة لا تُعدَّل: التصحيح بقيد عكسي (`is_reversal`, مبلغ سالب) والأصل يصبح `Reversed`. المجاميع = Approved + Reversed.
- فصل المهام: منشئ المعاملة أو الميزانية لا يعتمدها.
- `Idempotency-Key` على إنشاء المعاملات.
- ميزانية معتمدة واحدة لكل مشروع؛ اعتماد جديدة يُحلّ القديمة `Superseded`.
- EVM: PV خطي على الزمن التقويمي بين تواريخ الـ Baseline؛ EV = ميزانية البند × % الإنجاز الحالي؛ AC = Actual المعتمد حتى data date. المؤشرات `null` مع تحذير عند نقص المدخلات.

## الانحرافات عن ERD 1.0
- `Reversal` ليس قيمة في `transaction_type` بل `is_reversal` مع نوع الأصل.
- `budget_lines` بلا `project_id`: التحقق من نفس المشروع في الـ API.
- لا `Accrual` في العرض المالي المختصر؛ يُجمع منفصلًا.

## قيود معروفة / قرارات مفتوحة
- EV يستخدم سجل التقدم (migration 009) عند الـ data date؛ الأنشطة بلا سجل (بيانات قديمة) تستخدم النسبة الحالية مع تحذير.
- أي ميزانية غير مربوطة بنشاط في Baseline تقلل `budgetCoverage` ويصبح `isReliable=false`.
- لا تخفيف Commitment عند تسجيل Actual (تعريف "المتبقي" يحتاج قرارًا).
- فصل المهام مطبّق على المعاملات والميزانيات معًا (قد يصبح إعدادًا لكل شركة).
- لا Excel/PDF، لا واجهة Desktop للتكلفة، لا تعدد عملات.

## 0.8.1 — سجل التقدم
- `009_progress_updates.sql`: جدول `progress.progress_updates` للإضافة فقط (RLS + trigger + سحب UPDATE/DELETE).
- كل استدعاء لـ `PUT .../activities/{id}/progress` يسجّل صفًا بتاريخ `dataDate` (اختياري، الافتراضي اليوم UTC).
- EVM: آخر تحديث بتاريخ ≤ data date (التعادل لآخر تسجيل)؛ لا تحديث حتى ذلك التاريخ = 0%.
