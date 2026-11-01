# PROJEX 0.9.2-preview.1 — Step 1: Identity, Platform, RBAC

> **حالة التحقق:** لم يُبنَ ولم يُشغَّل بعد (لا يوجد .NET SDK ولا PostgreSQL في بيئة الكتابة). شغّل `dotnet build` ثم `dotnet test` (Docker مطلوب للاختبارات التكاملية) قبل الاعتماد على أي شيء هنا.

## 1أ — إصلاحات الأمان (migration 011)
- `identity.users` لها مفتاح مركّب `(tenant_id, id)`؛ والـ FKs من `user_memberships` و`user_sessions` و`project_memberships` صارت مركّبة، فلا يمكن ربط مستخدم من tenant آخر. (`project_memberships.user_id` لم يكن له FK أصلاً.)
- جداول Identity الفرعية (`user_roles`, `user_claims`, `user_logins`, `user_tokens`) عليها RLS مُجبَر تتبع مستخدمها.
- كل طلب بتوكن tenant يتحقق أن الـ tenant لا يزال `Active` (إيقاف tenant يقطع التوكنات الصادرة فورًا).
- Rate limiting على `login` و`refresh` و`bootstrap` (`Security:AuthRequestsPerMinute`، الافتراضي 20 لكل IP/دقيقة). خلف reverse proxy اضبط ForwardedHeaders.
- سياسة الباسورد والقفل قابلة للضبط من قسم `Security` (`PasswordMinLength` ≥ 10، `LockoutMaxAttempts` ≥ 3، `LockoutMinutes` ≥ 1، ...). الحدود الدنيا تمنع ضعف التكوين.
- **لم يُنفَّذ بعد:** تنظيف الجلسات المنتهية (يحتاج مهمة صيانة بدور owner لأن RLS يمنع الحذف العابر للـ tenants).

## 1ب — Platform (migration 012)
- `organization.tenants` ← `platform.tenants`؛ العمود `key` ← `code`؛ عمود جديد `default_locale`. لا حذف للـ tenants (`REVOKE DELETE`)؛ الحالة `Closed` نهائية.
- `platform.subscriptions`: اشتراك واحد حيّ لكل tenant (Trial/Active/PastDue)، `seat_limit`، `module_entitlements` (JSON).
- `platform.platform_admins`: مشغّلو المنصة. **ليسوا مستخدمي tenant** — توكنهم بلا `tenant_id`، فكل endpoints الـ tenant وسياسات RLS ترفضهم. العكس صحيح أيضًا.
- `platform.audit_events`: تدقيق للمنصة (إنشاء/إيقاف tenant، تغيير اشتراك، دخول المشغّلين) — append-only بـ trigger.

| Endpoint | الوصف |
|---|---|
| `POST /api/v1/platform/bootstrap` | أول مشغّل (مرة واحدة، بمفتاح `X-Bootstrap-Key`) |
| `POST /api/v1/platform/auth/login` | دخول المشغّل (توكن 15 دقيقة، بلا refresh) |
| `GET/POST /api/v1/platform/admins`, `POST .../{id}/disable|enable` | إدارة المشغّلين (لا يمكن تعطيل آخر مشغّل نشط) |
| `GET/POST /api/v1/platform/tenants`, `GET .../{id}` | قائمة/إنشاء tenant (شركة أولى + CompanyAdmin + اشتراك في معاملة واحدة) |
| `POST .../tenants/{id}/suspend|activate|close` | حالة الـ tenant |
| `GET/PUT .../tenants/{id}/subscription` | الاشتراك (لا يقل `seatLimit` عن المستخدمين النشطين) |

حدّ المقاعد يُفرض عند إنشاء مستخدم أو إعادة تفعيله؛ الاشتراك المعلّق/الملغي/المنتهي يمنع إضافة مستخدمين فقط (لا يمنع قراءة البيانات).

## 1ج — RBAC (migration 013)
- كتالوج صلاحيات (12) وأدوار نظام (8): CompanyAdmin, ProjectManager, PlanningEngineer, CostControlEngineer, Accountant, SiteEngineer, Viewer, Auditor. المصدر في الكود `RoleCatalog` ومطابق للـ seed (اختبار تكاملي يفشل عند أي اختلاف). كتالوج الصلاحيات للقراءة فقط لدور التطبيق، وأدوار النظام غير قابلة للتعديل.
- `identity.user_role_assignments`: دور على مستوى tenant كامل، أو شركة، أو فرع، أو مشروع (نطاق واحد فقط لكل تعيين). ترحيل تلقائي لمديري الشركات الحاليين.
- أدوار مخصّصة لكل tenant: مجموعات من الصلاحيات التي يطبّقها الخادم فعلاً على المشاريع (`projects.read`, `progress.update`, `planning.manage`, `costing.read|transactions.enter|manage|approve`). باقي الصلاحيات (`audit.read`, `reports.*`, `identity.manage`, `organization.manage`) محجوزة لأدوار النظام حتى تُفرض في endpoints.
- صلاحية جديدة `EnterCost` (إدخال/تقديم معاملات التكلفة) لدور Accountant؛ يملكها أيضًا PM وCostControl، فلا تراجع في الصلاحيات الحالية.
- الصلاحية الفعلية على مشروع = CompanyAdmin، أو دور عضوية المشروع، أو أي تعيين نطاقه يغطي المشروع (مع شرط العضوية في الشركة كما كان).
- إزالة دور على مستوى tenant كامل تُلغي جلسات المستخدم فورًا. لا يمكن سحب دور CompanyAdmin من آخر مدير نشط.

| Endpoint (CompanyAdmin فقط) | الوصف |
|---|---|
| `GET /api/v1/identity/permissions`, `GET .../roles` | الكتالوج والأدوار |
| `POST .../roles`, `PUT .../roles/{id}/permissions`, `DELETE .../roles/{id}` | أدوار مخصّصة |
| `GET/POST /api/v1/identity/users/{id}/roles`, `DELETE .../{assignmentId}` | تعيينات الأدوار |

## تغييرات كاسرة
- `POST /platform/bootstrap` لم يعد ينشئ tenant؛ استخدم `POST /platform/tenants`.
- `organization.tenants.key` ← `platform.tenants.code`.
- جداول ASP.NET Identity للأدوار (`identity_roles`, `user_roles`) لم تعد مصدر الأدوار (تُترك للتوافق).

## خطوات التحقق المقترحة
1. `dotnet build` ثم `dotnet test` (يشمل `RbacSchemaTests` و`RbacApiTests`).
2. تطبيق 011-013 على قاعدة فيها بيانات 0.9.1 والتأكد من ترحيل المديرين.
3. تجربة يدوية: bootstrap ← login منصة ← إنشاء tenant ← login مدير ← إنشاء دور مخصّص وتعيينه على مشروع.
