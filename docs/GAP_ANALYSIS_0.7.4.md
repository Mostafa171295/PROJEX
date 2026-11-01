# تحليل الفجوات — PROJEX ENTERPRISE 0.7.4 مقابل SRS 1.0 / ERD 1.0

## معايير القبول (SRS §12)
| # | المعيار | الحالة في 0.7.4 | بعد 0.8.0 |
|---|---------|-----------------|-----------|
| 1 | Tenant/شركة/فرع/مستخدمون + عزل | مكتمل جزئيًا (لا API لإدارة المستخدمين) | كما هو |
| 2 | Login + صلاحيات على الخادم | Login/JWT مكتمل؛ لا logout/refresh/MFA | + logout وrefresh وجلسات (0.9.0)؛ MFA ما زال ناقصًا |
| 3 | مشروع + WBS + أنشطة | مكتمل | — |
| 4–5 | علاقات/تقاويم/CPM + اختبارات مرجعية | مكتمل؛ تقويم واحد لكل شبكة | — |
| 6 | Gantt + Baseline | مكتمل؛ Baseline غير محمي في قاعدة البيانات | **008: حماية Baseline** |
| 7 | BOQ + ميزانية + Actual/Committed | **غير موجود** | **مُنفَّذ** |
| 8 | تقدم + EVM | تقدم لحظي فقط؛ لا EVM | **EVM مُنفَّذ** (بدون سجل تقدم تاريخي) |
| 9 | تقارير + Excel/PDF | **غير موجود** | تقرير JSON وCSV مُنفَّذان (0.9.1)؛ Excel وPDF مؤجَّلان |
| 10 | Audit | مُنفَّذ للعمليات الأساسية | + عمليات التكلفة |
| 11 | نسخ احتياطي/استعادة | **غير موجود** | غير منفَّذ |
| 12 | اختبارات Isolation/RBAC/Concurrency | Isolation فقط | + اختبارات immutability |
| 13 | Desktop على أنظمة متعددة | Gantt للقراءة/تعديل مدة فقط | — |

## ملاحظات على الكود الحالي
1. `Program.cs` ملف واحد (~65KB): يجب تقسيمه إلى وحدات Endpoints (بدأ ذلك بـ `Costing/` و`Security/ProjectAuthorizer`).
2. لا تحقق ببناء فعلي: لم يُبنَ أي إصدار بـ .NET SDK ولم تُشغَّل الـ migrations (يذكر README ذلك).
3. `004_project_memberships.sql`: المفتاح الأجنبي بدون `tenant_id` (غير مركّب).
4. FR-AUTH-002/003/004/005: لا logout ولا تجديد جلسة ولا MFA.
5. FR-IAM: لا API لإنشاء/تعطيل المستخدمين.
6. جداول `identity.roles/permissions` غير مستخدمة؛ الصلاحيات مشفّرة في `ProjectAccess`.
7. لا Departments/Templates/Documents/Subscriptions.
8. `Superseded` للـ Baseline لا يُضبط أبدًا.

## الترتيب المقترح بعد 0.8.0
1. بناء الحل فعليًا وتشغيل الاختبارات (CI) وإصلاح أي أخطاء.
2. سجل تقدم تاريخي `progress.progress_updates` (لتصحيح EV عند data date سابق).
3. المستخدمون/الجلسات/MFA، ثم Reporting وExcel/PDF.
4. تقسيم Program.cs، ثم الـ Desktop لشاشات التكلفة وEVM.
5. Backup/Restore وHardening.
