# Localization glossary, Phase 4 batches 14-21 (additions)

Fixed terms and plural notes for languages finished after the first glossary files. "Remindr" and "Remind UI" are never translated; "PIN" and "OK" stay as written.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative of each language, and wording is gender-neutral where the language allows it. Scripts: Latin for all five.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| et | meeldetuletus | Oluline | Edasta | Sulge | Tehtud | Profiil | PIN | Kategooria | Täna | teavitusala | teavitus |
| lv | atgādinājums | Svarīgi | Atlikt | Noraidīt | Pabeigt | Profils | PIN | Kategorija | Šodien | paziņojumu apgabals | paziņojums |
| lt | priminimas | Svarbu | Atidėti | Atmesti | Užbaigti | Profilis | PIN | Kategorija | Šiandien | sistemos dėklas | pranešimas |
| ga | meabhrúchán | Tábhachtach | Cuir siar | Dún | Críochnaigh | Próifíl | PIN | Catagóir | Inniu | tráidire | fógra |
| mt | promemoria | Importanti | Posponi | Għalaq | Temm | Profil | PIN | Kategorija | Illum | tray | notifika |

Plurals (CLDR): et uses One (1) and Other. lv uses Zero (0, 10, 20, 11-19; written as `_ZeroForm`, plus explicit `_Zero` for exactly 0), One (1, 21, ...) and Other. lt uses One (1, 21, ...), Few (2-9, 22-29, ...) and Other (0, 10-20, 30, ...). ga uses One (1), Two (2), Few (3-6), Many (7-10) and Other. mt uses One (1), Two (2), Few (0, 3-10, ...), Many (11-19) and Other. Every language also has the explicit `_Zero` text where the English has it.

Date patterns are localized (day-month order, dotted days for et and lv, year-first for lt); `Fmt_*` values are valid .NET patterns, a lone `d` is written `%d`.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use verbal nouns (tt, ba, sah, os) or the imperative (ce). All five use Cyrillic; ce writes the palochka as Ӏ (U+04C0).

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| tt | искәртмә | мөһим | кичектерү | ябу | тәмамлау | профиль | PIN | төркем | бүген | трей | белдерү |
| ba | иҫкәртмә | мөһим | кисектереү | ябыу | тамамлау | профиль | PIN | төркөм | бөгөн | трей | белдереү |
| sah | өйдөтүү | суолталаах | хойутатыы | сабыы | бүтэрии | профиль | PIN | бөлөх | бүгүн | трей | биллэрии |
| ce | дагалоцам | коьрта | тӀаьхьадаккха | дӀакъовла | чекхдаккха | профиль | PIN | тайпа | тахана | трей | хаам |
| os | æрымысæн | ахсджиаг | фæстæдæр | æхгæнын | сæххæст кæнын | профиль | PIN | къорд | абон | трей | хабар |

Plurals (CLDR): ce and os use One (1) and Other. tt, ba and sah have no One/Other distinction in the app, so they always use Other; their One and Other texts are identical and number-neutral (the noun stays singular after a number). Every language also has the explicit Zero text.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Scripts: Ethiopic (ti), Tifinagh (zgh), Vai syllabary (vai-Vaii), Cherokee syllabary (chr), Canadian Aboriginal syllabics (iu-Cans), Yi syllabary (ii). Low-resource languages: every file here needs native review.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| ti | መዘኻኸሪ | ኣገዳሲ | ደሓር ዘኻኽረኒ | ዕጸው | ወድእ | ፕሮፋይል | PIN | ክፍሊ | ሎሚ | ትሬይ | ሓበሬታ |
| zgh | ⴰⵙⵙⵛⴼⵓ | ⵉⵎⵓⵀⵉⵎ | ⵙⵙⵓⵔⵔⵓ | ⵙⴷⴷ | ⵙⴽⵎⵍ | ⴱⵔⵓⴼⵉⵍ | PIN | ⵜⴰⵙⵏⵉⴼⵜ | ⴰⵙⵙⴰ | ⵜⵔⴰⵢ | ⵜⴰⵙⵙⴰⵡⴰⵍⵜ |

Notes on sources: ti, zgh and iu-Cans use real vocabulary where known (Tigrinya, Tamazight and Inuktitut roots); many technical terms in zgh are loans or coinages. vai-Vaii, chr and ii are mostly phonetic loan renderings of the English or Mandarin technical word written in the native syllabary (Yi uses Mandarin-based sounds), with a few native words (Cherokee ᎪᎯ ᎢᎦ today, ᏑᎾᎴᎢ tomorrow, ᎣᏏ good; Vai dondo "one"; Yi hxop "language"). They are comprehensible placeholders, not polished translations.

Plurals (CLDR): ti uses One (0 and 1) and Other; chr uses One (1) and Other; iu uses One (1), Two (2) and Other (the `_Two` keys exist for Calendar_DayCaption, Calendar_DayAutomationCount and Tray_TooltipDue). zgh, vai-Vaii and ii have no CLDR rule in the app, so they always use Other; their One and Other texts are written number-neutral. Every language also has the explicit Zero text.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative of each language, and wording is gender-neutral where the language allows it. Scripts: Malayalam (ml), Sinhala (si), Ol Chiki (sat-Olck), Tibetan (bo, dz).

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| ml | ഓർമ്മപ്പെടുത്തൽ | പ്രധാനം | സ്നൂസ് ചെയ്യുക | ഒഴിവാക്കുക | പൂർത്തിയാക്കുക | പ്രൊഫൈൽ | PIN | വിഭാഗം | ഇന്ന് | ട്രേ | അറിയിപ്പ് |
| si | මතක් කිරීම | වැදගත් | ස්නූස් කරන්න | ඉවත දමන්න | සම්පූර්ණ කරන්න | පැතිකඩ | PIN | ප්‍රවර්ගය | අද | ට්‍රේ | දැනුම්දීම |
| sat-Olck | ᱨᱤᱢᱟᱭᱱᱰᱚᱨ | ᱞᱟᱹᱠᱛᱤᱭᱟᱱ | ᱥᱱᱩᱡ ᱢᱮ | ᱵᱚᱱᱫᱚ ᱢᱮ | ᱯᱩᱨᱟᱹᱣ ᱢᱮ | ᱯᱨᱚᱯᱷᱟᱭᱤᱞ | PIN | ᱫᱚᱞ | ᱱᱚᱣᱟ ᱫᱤᱱ | ᱴᱨᱮ | ᱥᱩᱪᱚᱱᱟ |
| bo | དྲན་སྐུལ། | གལ་ཆེན། | ཕྱིས་སུ་སྐུལ། | སྒོ་རྒྱོབ། | མཇུག་སྒྲིལ། | སྒེར་གྱི་ཡིག་ཆ། | PIN | རིགས། | དེ་རིང་། | མ་ལག་སྒམ། (ཚིག་ "སྒམ") | བརྡ་ཐོ། |
| dz | དྲན་སྐུལ། | གལ་ཆེན། | ཕྱིས་སུ་སྐུལ། | སྒོ་རྒྱོབ། | མཇུག་སྒྲིལ། | སྒེར་གྱི་ཡིག་ཆ། | PIN | རིགས། | དེ་རིང་། | སྒམ | བརྡ་ཐོ། |

Plurals (CLDR): ml uses One (1) and Other; si uses One (0 and 1) and Other; sat-Olck uses One (1), Two (2) and Other, and the Two texts are the same as One and Other because Santali does not mark number on the noun; bo and dz have no plural distinction (CLDR "other" only), so their One and Other texts read correctly for any number. Every language also has the explicit Zero text.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative where the language has one. All six are low-resource languages: terms are best-effort, several borrow Spanish words (perfil, bandeja), and none has been checked by a native speaker.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| gn | Mandu'arã | Tekotevẽva | Eha'arõ | Emboyke | Emohu'ã | Perfil | PIN | Aty | Ko ára | Bandeja | Marandu |
| mi | Whakamahara | Hira | Whakatārewa | Kati | Whakaoti | Kōtaha | PIN | Kāwai | Tēnei rā | Pae pūnaha | Whakamōhiotanga |
| haw | Hoʻomanaʻo | Koʻikoʻi | Hoʻopaneʻe | Hoʻokuʻu | Hoʻopau | Moʻolelo | PIN | Māhele | I kēia lā | Pā ʻōnaehana | Hōʻike |
| qu | Yuyachiy | Chaniyuq | Qhipaman churay | Wisq'ay | Tukuy | Perfil | PIN | Laya | Kunan | Bandeja | Willay |
| quc | Na'ojib'al | Nim rejqalem | Jub'a' chik | Tiyäk | Tik'is | Perfil | PIN | Wokaj | Wakami q'ij | Bandeja | Tzijonem |
| arn | Rakiduam | Ngüneduam | Wüla | Mütrüm | Küpalen | Perfil | PIN | Rulpa | Fachantü | Bandeja | Ngülamtun |

Plurals (CLDR): haw uses One (1) and Other. gn, mi, qu, quc and arn have no CLDR rule in the app, so they always use Other; their One and Other texts are written number-neutral (the noun is not inflected for number) so both read correctly. Every language also has the explicit Zero text.

Dates: haw keeps the English month-first patterns; gn, mi, qu, quc and arn use day-first patterns (`d MMM yyyy`).


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative of each language, and wording is gender-neutral where the language allows it. Scripts: Latin for all five (Hausa with hooked letters, Yoruba with tone marks and under-dots, Igbo with dot-below vowels).

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| ha | mai tunatarwa | mai muhimmanci | Jinkirta | Yi watsi | Kammala | bayanan kai | PIN | rukuni | Yau | tire | sanarwa |
| yo | ìránnilétí | pàtàkì | Sún síwájú | Pa tì | Parí | pírófáìlì | PIN | ẹ̀ka | Òní | àtẹ ẹ̀rọ | ìkéde |
| ig | ncheta | dị mkpa | Chere | Chefuo | Mechaa | profaịlụ | PIN | otu | Taa | tray | ọkwa |
| zu | isikhumbuzo | kubalulekile | Hlehlisa | Yeka | Qeda | iphrofayela | PIN | isigaba | Namuhla | ithreyi | isaziso |
| xh | isikhumbuzi | ibalulekile | Libazisa | Yeka | Gqibezela | iprofayile | PIN | udidi | Namhlanje | itreyi | isaziso |

Plurals (CLDR): zu uses One (0 and 1) and Other; ha and xh use One (1) and Other; yo and ig have no distinguishing rule, so they always use Other and their One and Other texts are written number-neutral ("Ncheta: {0}"). Every language also has the explicit Zero text. Date patterns are day-first ("d MMM"), the same as the Afrikaans file.

Quality note: these are low-resource languages and the texts are machine-assisted; a native-speaker review is recommended, especially for ha "bayanan kai" (profile), yo "àtẹ ẹ̀rọ" (tray) and zu/xh "Yeka" (dismiss).


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative of each language, and wording is gender-neutral. Script: Latin for all five.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| rw | Ikibutsa | Ingenzi | Subika | Funga | Rangiza | Umwirondoro | PIN | Icyiciro | Uyu munsi | gice cya sisitemu | Imenyesha |
| rn | Icibutsa | Ngirakamaro | Subika | Funga | Rangiza | Umwirondoro | PIN | Iciciro | Uno musi | gice ca sisitemu | Ikimenyeshwa |
| sn | Chirangaridzo | Zvakakosha | Dzorera shure | Vhara | Pedzisa | Purofaira | PIN | Rudzi | Nhasi | tirei | Chiziviso |
| lg | Ekijjukiza | Kikulu | Zzaayo | Ggalawo | Maliriza | Puloofayiro | PIN | Ekibinja | Leero | tray | Okumanyisa |
| wo | Fàttali | Am solo | Yeexal | Bàyyi | Mottali | Profil | PIN | Xeet | Tey | tray | Yëgle |

Plurals (CLDR): sn and lg use One (1) and Other. rw, rn and wo have no rule in the app, so they always use Other; their One and Other texts are written number-neutral ("Ibyibutsa: {0}", "Fàttali: {0}") so both read correctly. Every language also has the explicit Zero text.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative, gender-neutral where possible. All five use the Latin script.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| ff-Latn | Siftinirgol | Ko mawni | Raartu | Haɗu | Timmin | Profil | PIN | Fedde | Hannde | tray | Habaru |
| mg | Fampahatsiahivana | Manan-danja | Aoriana kely | Ariana | Vita | Mombamomba | PIN | Sokajy | Anio | tray | Fampandrenesana |
| ts | Xitsundzuxo | Xa nkoka | Yimela | Tshika | Hetisa | Phrofayili | PIN | Xiyenge | Namuntlha | tray | Xitiviso |
| ss | Sikhumbuto | Kubalulekile | Hlehlisa | Cashisa | Phelelisa | Iphrofayela | PIN | Sigaba | Lamuhla | tray | Sazisi |
| nd | Isikhumbuzo | Kubalulekile | Hlehlisa | Yekela | Qeda | Iphrofayili | PIN | Isigaba | Lamuhla | tray | Isaziso |

Plurals (CLDR): ff uses One (0 and 1) and Other; mg uses One (0 and 1) and Other; ts, ss and nd use One (1) and Other. Every language also has the explicit Zero text. Plural forms: ff siftinirgol / siftinirɗe; mg fampahatsiahivana (same form for both); ts xitsundzuxo / switsundzuxo; ss sikhumbuto / tikhumbuto; nd isikhumbuzo / izikhumbuzo.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative where the language has one. Script: Latin for all five (extended letters: ɔ ɛ ɲ ŋ õ for bm and dje; ä ë ï ö ü â ê î ô û for sg).

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| luo | Paro | Duongʼ | Dwogi | Kwer | Tiek | Profail | PIN | Kit | Kawuono | tray | Ladhi |
| dje | Fongandi | Beeri | Dira banda | Furo | Ban | Porofil | PIN | Dumi | Hõ | tray | Habaru |
| bm | Hakilijigin | Nafa ka bon | Dɛsɛ | Bila | A ban | Porofili | PIN | Suguya | Bi | tray | Kunnafoni |
| sg | Bi-li | Kota | Ngbâ | Zi | Fini | Profîl | PIN | Kâ | Lâ so | tray | Tënë |

Plurals (CLDR): bm and sg have the "other only" rule; luo, kln and dje have no CLDR rule in the app, so they always use Other. One and Other texts are written number-neutral (for example "Paro: {0}") so both read correctly for any count. Every language also has the explicit Zero text.

Quality note: kln, dje, luo and sg are very low-resource for machine translation; all five files need native review.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative of each language. Scripts: Latin for all six.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| nyn | Ekyokwijutsya | Kikuru | Ijutsya bwanyima | Reka | Maliriza | Porofayiro | PIN | Ekika | Erizooba | entebe y'esisiteemu | Okumanyisa |
| lb | Erënnerung | Wichteg | Spéider erënneren | Zoumaachen | Erledegen | Profil | PIN | Kategorie | Haut | Systemleescht | Notifikatioun |
| oc | Rapèl | Prioritari | Repausar | Ignorar | Acabar | Perfil | PIN | Categoria | Uèi | zòna de notificacion | Notificacion |
| rm | Regord | Impurtant | Repeter pli tard | Serrar | Terminar | Profil | PIN | Categoria | Oz | area da notificaziun | Notificaziun |
| gd | Cuimhneachan | Cudromach | Cuir dàil air | Leig seachad | Cuir crìoch air | Pròifil | PIN | Roinn-seòrsa | An-diugh | treidhe an t-siostaim | Brath |

Plurals (CLDR): lb, nyn, rm and teo use One (1) and Other. gd uses One (1, 11), Two (2, 12), Few (3-10, 13-19) and Other; all four texts are identical because the number-neutral form "Cuimhneachain: {0}" is correct for any count. oc has no CLDR rule in the app, so it always uses Other; its One and Other texts are number-neutral ("Rapèls : {0}"). nyn and teo counts are also written number-neutral ("Ebyokwijutsya: {0}", "Akimuj: {0}") because noun-class agreement is not modelled. Every language also has the explicit Zero text. Calendar_Agenda is "Lëscht" (lb), "Ordre del jorn" (oc), "Program" (rm), "Clàr-gnothaich" (gd), "Enteekateeka" (nyn), "Ekitiyo" (teo). Date patterns are day-first; Remindr dates use the .NET culture's own month names.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the polite imperative of each language. Scripts: Latin for all five.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| dsb | pśipomnjeśe | Wažne | Dremotaś | Zachyśiś | Dokóńcyś | Profil | PIN | Kategorija | Źinsa | systemowe pólo | Powěsć |
| se | muittuhus | Deaŧalaš | Gaskkalda | Gaskkalduhte | Gárvvis | Profiila | PIN | Kategoriija | Odne | vuogádatbeavdi | Dieđiheapmi |
| kw | daskov | Poesek | Hunros | Hedhi | Gorfenna | Profil | PIN | Klass | Hedhyw | tray | Derivas |
| gv | cooinaghtyn | Ymmydoil | Fo-ghooinney | Cur ersooyl | Jean | Pròfeil | PIN | Rolley | Jiu | claare-lhiattee | Fysseree |
| nds | Erinnern | Wichtig | Slummern | Wegdrücken | Afsluten | Profil | PIN | Kategorie | Vundaag | Systemleist | Mellen |

Plurals (CLDR): dsb uses One (n%100=1), Two (2), Few (3-4) and Other; se uses One, Two and Other; gv uses One, Two, Few (n%100 = 0, 20, 40, 60, 80) and Other; kw uses Zero, One, Two, Few, Many and Other. kw and gv put the noun in the singular after numerals, so those forms differ little. nds has no CLDR rule in the app, so it always uses Other; its One and Other texts are number-neutral ("Erinnerns: {0}"). Every language also has the explicit Zero text for the day caption and automation count (kw also for the tray tooltip).


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Wording is Dzongkha, not Tibetan: the polite imperative uses གནང་, and sentences end in ཨིན, ཨིནམ or ཡི with the Dzongkha particles ལུ and ཚུ. Script: Tibetan (Uchen).

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| dz | དྲན་གསོ | གལ་ཅན | ཤུལ་ལས་དྲན་གསོ་འབད | བཀོ་བཞག | མཇུག་བསྡུ | གསལ་སྡུད | PIN | སྡེ་ཚན | ད་རིས | སྡེར་མ | བརྡ་བསྐུལ |

Plurals (CLDR): dz has only Other, so it always uses Other. Its One and Other texts are the same number-neutral wording (དྲན་གསོ་ {0}), and it also has the explicit Zero text.

iu-Cans is not covered here: no new translation was written for it (see the part M report).


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Latin buttons use the singular imperative, the normal form for addressing one reader; Tshivenḓa buttons use the polite -ani imperative. Scripts: Latin for both; Tshivenḓa uses its dotted and circumflexed letters (ḓ ḽ ṅ ṋ ṱ).

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| la | monitum | Magni momenti | Differ | Dimitte | Perfice | Persona | PIN | Categoria | Hodie | tabella systematis | Nuntius |
| ve | khumbudzo | Zwa ndeme | Lengisani | Litshani | Fhedzani | Phurofaiḽi | PIN | Khethekanyo | Ṋamusi | thirei ya sisiṱeme | Ndivhadzo |

Other fixed terms. la: update renovatio, download depromere, install instituere, version editio, button globulus, settings Optiones; "Agenda" is Ordo rerum and "Color" is Color personae, to avoid the spelling shared with English. ve: update khwiniso, download dawunuḽoda, install longa, version vesheni, button bathani, settings Dzisethingi, agenda mbekanyamushumo.

Plurals (CLDR): ve uses One (1) and Other. la has no CLDR rule in the app, so it always uses Other; its One and Other texts are number-neutral ("Monita: {0}"). Both languages also have the explicit Zero text.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. Buttons use the plain imperative, the normal register for software in both languages, and wording is gender-neutral where the language allows it. Sardinian follows Limba Sarda Comuna (LSC), not Logudorese or Campidanese. Papiamento follows the Curaçao spelling.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sc | ammentu | importante | postpone | iscarta | cumpleta | profilu | PIN | categoria | oe | àrea de notìficas | notìfica |
| pap | rekordatorio | importante | posponé | deskartá | kompletá | perfil | PIN | kategoria | awe | área di notifikashon | notifikashon |

Plurals (CLDR): sc uses One (1) and Other; pap uses One (1) and Other. Both also have the explicit Zero text. Papiamento does not mark a plural after a number, so its One and Other texts are the same ("{0} rekordatorio"); the plural marker -nan appears only without a number.


"Remindr" and "Remind UI" are never translated. "PIN", "OK", file names and `{0}` placeholders stay as written. South Ndebele (nr) is written in Latin script with its own spelling (tj, tjh, kgh, tl, tlh, rh), the relative suffix -ko and iiN- plurals. Chuvash (cv) is written in Cyrillic with ӑ ӗ ҫ ӳ.

| Language | Reminder | Important | Snooze | Dismiss | Complete | Profile | PIN | Category | Today | Tray | Notification |
|---|---|---|---|---|---|---|---|---|---|---|---|
| nr | isikhumbuzo (pl. iinkhumbuzo) | kuqakathekile | Riyada | Tjhiya | Qeda | iphrofayili | PIN | umhlobo | Namhlanje | ithreyi | isaziso (pl. iinaziso) |
| cv | асаилтерӳ | пӗлтерӗшлӗ | Кая юлтар | Хуп | Вӗҫле | профиль | PIN | категори | Паян | трей | пӗлтерӳ |

Plurals (CLDR): nr uses One (1) and Other, with its own singular and plural noun forms. cv has no CLDR rule in the app, so it always uses Other; a Chuvash noun stays singular after a numeral, so its One and Other texts are the same and read correctly for any number. Both languages also have the explicit Zero text.
