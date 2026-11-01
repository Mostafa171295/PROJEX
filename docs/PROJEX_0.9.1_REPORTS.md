# PROJEX 0.9.1 — Reports (JSON + CSV)

لم يُبنَ ولم يُشغَّل. لا migration جديد ولا حزم NuGet جديدة.

- `GET /api/v1/projects/{id}/reports/overview` — جدول الأنشطة (حسب الحالة، الحرجة المفتوحة، المتأخرة، متوسط الإنجاز، آخر حساب) + التكلفة (BAC، فعلي، التزامات، بانتظار الاعتماد، آخر لقطة EVM) **للمخوّلين بـ ReadCost فقط**. يتضمن وقت الإنشاء ومعايير التصفية.
- `GET .../reports/boq.csv` و`.../reports/transactions.csv?fromDate=&toDate=` — UTF-8 مع BOM (للعربية في Excel)، حماية من حقن الصيغ (`=`,`+`,`-`,`@`)، وتُسجَّل في التدقيق (`reports.exported`). الحد الأقصى 50,000 صف.

**مؤجَّل عمدًا:** تصدير Excel (.xlsx) وPDF يحتاجان ClosedXML وQuestPDF. سأضيفهما بعد أن يمر البناء الحالي، حتى لا أضيف تبعيات لم يتحقق منها أحد.
