# Localization glossary, batch 3 (ar, he, hi, bn, th, ja, ko, zh-Hans, zh-Hant)

"Remindr" is never translated. "PIN" is kept in Latin letters in every language (ar and he add a noun: "رمز PIN", "קוד PIN").

| Term | ar | he | hi | bn | th |
|---|---|---|---|---|---|
| Reminder | تذكير | תזכורת | रिमाइंडर | রিমাইন্ডার | การเตือนความจำ |
| Important | مهم | חשוב | महत्वपूर्ण | গুরুত্বপূর্ণ | สำคัญ |
| Profile | الملف الشخصي | פרופיל | प्रोफ़ाइल | প্রোফাইল | โปรไฟล์ |
| PIN | رمز PIN | קוד PIN | PIN | PIN | PIN |
| Category | الفئة | קטגוריה | श्रेणी | বিভাগ | หมวดหมู่ |
| Calendar | التقويم | לוח שנה | कैलेंडर | ক্যালেন্ডার | ปฏิทิน |
| Agenda | جدول الأعمال | סדר יום | एजेंडा | এজেন্ডা | กำหนดการ |
| Week | الأسبوع | שבוע | सप्ताह | সপ্তাহ | สัปดาห์ |
| Month | الشهر | חודש | माह | মাস | เดือน |
| Today | اليوم | היום | आज | আজ | วันนี้ |

| Term | ja | ko | zh-Hans | zh-Hant (Taiwan) |
|---|---|---|---|---|
| Reminder | リマインダー | 미리 알림 | 提醒 | 提醒 |
| Important | 重要 | 중요 | 重要 | 重要 |
| Profile | プロファイル | 프로필 | 配置文件 | 設定檔 |
| PIN | PIN | PIN | PIN | PIN |
| Category | カテゴリ | 범주 | 类别 | 類別 |
| Calendar | カレンダー | 캘린더 | 日历 | 行事曆 |
| Agenda | アジェンダ | 일정 | 日程 | 議程 |
| Week | 週 | 주 | 周 | 週 |
| Month | 月 | 월 | 月 | 月 |
| Today | 今日 | 오늘 | 今天 | 今天 |

## Notes

- Plural variants: ar has Zero/One/Two/Few/Many/Other, he has Zero/One/Two/Other (Zero is a "no reminders" phrase without a number). ja, ko, zh-Hans, zh-Hant, th, hi and bn reuse the same wording for One and Other. One/Two values keep `{0}` because the placeholder test requires the same indices as English.
- Date patterns: ja/zh quote the literal characters, e.g. `M'月'd'日' dddd`; ko uses `M'월' d'일'`. hi/bn/th use `d MMM yyyy` order. ar uses the Arabic comma (،) after the weekday.
- Thai date patterns rely on the th-TH culture (Buddhist calendar years, 2569).
