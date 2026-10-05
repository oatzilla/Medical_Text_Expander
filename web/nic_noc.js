// ==========================================================================
// Medical Text Expander - International Standard NANDA-I, NIC, NOC Module
// Version: 1.9.6
// ==========================================================================

const NIC_NOC_CATALOG = [
  {
    code: "00132",
    name: "ปวดเฉียบพลัน (Acute Pain)",
    domain: "12. Comfort (ความสุขสบาย)",
    category: "ความปวดและการบรรเทา",
    relatedTo: "การบาดเจ็บของเนื้อเยื่อจากการผ่าตัด / กระดูกหัก / การอักเสบเฉียบพลัน",
    subjective: "ผู้ป่วยบ่นปวดแผลผ่าตัด Pain score 7/10 ลักษณะปวดตึงแน่นและแปล๊บๆ บริเวณรอยโรค",
    objective: "สีหน้าหน้านิ่วคิ้วขมวด มีอาการกระสับกระส่าย V/S: BP 138/85 mmHg, PR 92 bpm",
    goal: "ระดับความปวดลดลง (Pain Level: NOC 2102) - Pain score ≤ 3/10 ภายใน 30-60 นาที, สีหน้าผ่อนคลาย, สามารถนอนหลับพักผ่อนได้",
    interventions: [
      { text: "ประเมินระดับความปวด ตำแหน่ง ลักษณะ และ Pain score (0-10) ทุก 4 ชั่วโมง และประเมินซ้ำหลังให้ยาบรรเทาปวด 30-60 นาที", shift: "all", checked: true },
      { text: "จัดท่านอนที่ช่วยลดแรงดึงรั้งของแผลผ่าตัด (Semi-Fowler's position) และหนุนหมอนรองรับส่วนที่บาดเจ็บให้อยู่ในแนวปกติ", shift: "all", checked: true },
      { text: "ดูแลให้ได้รับยาบรรเทาปวดตามแผนการรักษาของแพทย์อย่างตรงเวลา และติดตามอาการข้างเคียง (คลื่นไส้, เวียนศีรษะ, ง่วงซึม)", shift: "all", checked: true },
      { text: "สอนและแนะนำเทคนิคการผ่อนคลาย (Deep breathing exercise) และการเบี่ยงเบนความสนใจ (Distraction) ขณะมีอาการปวด", shift: "morning", checked: true },
      { text: "ประคบเย็น/อุ่นบริเวณรอบรอยโรคตามข้อบ่งชี้ทางการแพทย์เพื่อลดอาการบวมและลดการส่งสัญญาณความปวด", shift: "afternoon", checked: false },
      { text: "จัดสิ่งแวดล้อมให้เงียบสงบ ลดแสงไฟและเสียงรบกวนในเวลากลางคืนเพื่อส่งเสริมการนอนหลับพักผ่อน", shift: "night", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): Pain score ลดลงเหลือ 2/10 หลังได้รับยา 30 นาที นอนหลับพักผ่อนได้ สีหน้าผ่อนคลาย ไม่มีอาการคลื่นไส้อาเจียน"
  },
  {
    code: "00085",
    name: "การเคลื่อนไหวร่างกายบกพร่อง (Impaired Physical Mobility)",
    domain: "4. Activity/Rest (กิจกรรมและการพักผ่อน)",
    category: "การเคลื่อนไหวและกระดูก",
    relatedTo: "ภาวะหลังผ่าตัดกระดูกและข้อ / ใส่เฝือก / ความปวด / กล้ามเนื้ออ่อนแรง",
    subjective: "ผู้ป่วยบอกขยับตัวลำบาก กลัวแผลแยก กลัวเจ็บเวลาขยับขาข้างที่ผ่าตัด",
    objective: "นอนอยู่บนเตียง ต้องพึ่งพาผู้ดูแลในการทำกิจวัตรประจำวัน มีข้อจำกัดในการงอและเหยียดข้อ ใส่เฝือก/Slab",
    goal: "การเคลื่อนไหว (Mobility: NOC 0208) - สามารถพลิกตะแคงตัวและบริหารกล้ามเนื้อได้ถูกต้อง, ลุกนั่งข้างเตียงและทรงตัวได้ปลอดภัย, ไม่เกิดข้อติดแข็ง",
    interventions: [
      { text: "สอนและกระตุ้นการออกกำลังกล้ามเนื้อแบบเกร็งค้าง (Isometric exercise / Ankle pumping) 10-15 ครั้ง/รอบ วันละ 3-4 รอบ", shift: "morning", checked: true },
      { text: "ช่วยเหลือและกระตุ้นการพลิกตะแคงตัวเปลี่ยนท่าอย่างน้อยทุก 2 ชั่วโมง เพื่อป้องกันภาวะแทรกซ้อนจากการนอนนิ่ง", shift: "all", checked: true },
      { text: "ช่วยเหลือในการฝึกทำกิจวัตรประจำวันเท่าที่ทำได้ (Self-care assistance) เพื่อส่งเสริมความมั่นใจและการฟื้นตัว", shift: "all", checked: true },
      { text: "ประสานงานร่วมกับนักกายภาพบำบัดในการฝึกใช้อุปกรณ์ช่วยเดิน (Walker / Crutches) ตามระดับ Weight bearing ที่แพทย์อนุญาต", shift: "morning", checked: true },
      { text: "จัดท่าและแนวลำตัว (Body alignment) ให้อยู่ในท่าที่ถูกต้องเพื่อป้องกันการหดรั้งของข้อและกล้ามเนื้อ", shift: "night", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ผู้ป่วยสามารถทำ Ankle pumping exercises ได้ถูกต้อง ลุกนั่งข้างเตียงได้โดยไม่มีอาการหน้ามืดเวียนศีรษะ"
  },
  {
    code: "00155",
    name: "เสี่ยงต่อการพลัดตกหกล้ม (Risk for Falls)",
    domain: "11. Safety/Protection (ความปลอดภัย)",
    category: "ความปลอดภัยของผู้ป่วย",
    relatedTo: "ภาวะหลังผ่าตัด, ได้รับยาแก้ปวด/ยานอนหลับ, อายุมาก, แขนขาอ่อนแรง",
    subjective: "ผู้ป่วยบอกขาล้า ทรงตัวยังไม่มั่นคง มีอาการมึนศีรษะเล็กน้อยหลังได้ยา",
    objective: "ประเมิน Morse Fall Scale = 65 คะแนน (เสี่ยงสูง), มีแผลผ่าตัดขา, สวมชุดผู้ป่วยและมีสายน้ำเกลือ",
    goal: "พฤติกรรมความปลอดภัย (Fall Prevention Behavior: NOC 1909) - ผู้ป่วยและญาติปฏิบัติตามมาตรการป้องกันการพลัดตกหกล้มได้ถูกต้อง ไม่เกิดอุบัติเหตุหกล้มตลอดเวร",
    interventions: [
      { text: "ยกไม้กั้นเตียงขึ้นทั้ง 2 ข้างตลอดเวลา และตรวจสอบให้แน่ใจว่าล็อกล้อเตียงแน่นหนาเรียบร้อย", shift: "all", checked: true },
      { text: "ติดป้ายสัญลักษณ์แจ้งเตือน 'ระวังพลัดตกหกล้ม (Fall Risk)' ที่หัวเตียง และสวมสายรัดข้อมือสีแจ้งเตือน", shift: "all", checked: true },
      { text: "จัดวางกริ่งเรียกพยาบาล (Call bell) และของใช้จำเป็นให้อยู่ในระยะที่ผู้ป่วยเอื้อมหยิบถึงได้สะดวก", shift: "all", checked: true },
      { text: "ให้สุขศึกษาแก่ผู้ป่วยและญาติ: เน้นย้ำไม่ให้ลุกจากเตียงตามลำพัง และให้กดกริ่งเรียกพยาบาลช่วยเหลือทุกครั้งก่อนลุก", shift: "morning", checked: true },
      { text: "เปิดไฟส่องสว่างนำทางในห้องพักและทางเดินไปห้องน้ำอย่างเพียงพอ ตรวจสอบพื้นที่ไม่มีน้ำเปียกหรือสิ่งกีดขวาง", shift: "night", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ไม้กั้นเตียงยกขึ้นตลอดเวลา ผู้ป่วยและญาติกดกริ่งเรียกพยาบาลทุกครั้งก่อนลุก ไม่เกิดเหตุการณ์พลัดตกหกล้มในเวร"
  },
  {
    code: "00004",
    name: "เสี่ยงต่อการติดเชื้อ (Risk for Infection)",
    domain: "11. Safety/Protection (ความปลอดภัย)",
    category: "การติดเชื้อและการหายของแผล",
    relatedTo: "มีแผลผ่าตัด, คาสายสวนปัสสาวะ, มีสายระบายเลือด (Drain), คาสายน้ำเกลือ",
    subjective: "ผู้ป่วยบอกไม่เจ็บแผลเพิ่มขึ้น ไม่มีอาการหนาวสั่น",
    objective: "แผลผ่าตัดเย็บปิดด้วย Staple ยาว 10 cm, มี Redivac drain ต่อขวดสุญญากาศ, อุณหภูมิกาย 36.8 C, WBC 7,800 /uL",
    goal: "การควบคุมการติดเชื้อ (Infection Severity: NOC 0702) - ไม่มีไข้ (Body temp 36.5-37.4 C), แผลผ่าตัดแห้งดี ไม่มีเลือดหรือหนองซึม, สัญญาณชีพปกติ",
    interventions: [
      { text: "ปฏิบัติตามหลักปราศจากเชื้อ (Aseptic technique) อย่างเคร่งครัดขณะทำแผลและเทสิ่งคัดหลั่งจากสายระบาย", shift: "all", checked: true },
      { text: "ตรวจประเมินลักษณะแผลผ่าตัด อาการปวด บวม แดง ร้อน และสังเกตเลือด/สิ่งคัดหลั่งที่ซึมออกมา", shift: "morning", checked: true },
      { text: "ดูแลระบบสายระบายให้อยู่ในระบบปิดสุญญากาศ (Closed vacuum drainage) บันทึกลักษณะและปริมาณสิ่งคัดหลั่งทุกเวร", shift: "all", checked: true },
      { text: "ดูแลทำความสะอาดรอบรอยแทงสาย IV และสาย Foley catheter ให้แห้งสะอาด ปราศจากสิ่งสกปรก", shift: "all", checked: true },
      { text: "ดูแลให้ได้รับยาปฏิชีวนะ (Antibiotics) ทางหลอดเลือดดำตามแผนการรักษาตรงเวลาอย่างเคร่งครัด", shift: "all", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): แผลผ่าตัดแห้งดี ไม่มีเลือดหรือหนองซึม อุณหภูมิกาย 36.8 C สัญญาณชีพปกติ สายระบายต่อระบบปิดเรียบร้อย"
  },
  {
    code: "00047",
    name: "เสี่ยงต่อผิวหนังถูกทำลาย / แผลกดทับ (Risk for Impaired Skin Integrity)",
    domain: "11. Safety/Protection (ความปลอดภัย)",
    category: "ผิวหนังและแผลกดทับ",
    relatedTo: "นอนนิ่งบนเตียงเป็นเวลานาน, เคลื่อนไหวร่างกายไม่ได้, มีแรงกดทับและแรงเฉือน",
    subjective: "ผู้ป่วยบอกเมื่อยหลังและสะโพก แต่ไม่มีอาการเจ็บแสบผิวหนัง",
    objective: "ประเมิน Braden Scale = 13 คะแนน (เสี่ยงปานกลางถึงสูง), นอนบนเตียงตลอดเวลา, ผิวหนังแห้ง",
    goal: "สภาพความสมบูรณ์ของผิวหนัง (Tissue Integrity: NOC 1101) - ผิวหนังบริเวณปุ่มกระดูกไม่มีรอยแดง (Non-blanchable erythema), ไม่เกิดแผลกดทับตลอดการรักษา",
    interventions: [
      { text: "พลิกตะแคงตัวเปลี่ยนท่าผู้ป่วยอย่างน้อยทุก 2 ชั่วโมง โดยจัดท่านอนตะแคงกึ่งหงายทำมุม 30 องศา", shift: "all", checked: true },
      { text: "ใช้ที่นอนลมลดแรงกดทับ (Alternating pressure air mattress) และใช้หมอนนุ่มรองใต้ขารองรับส้นเท้าให้ลอยพ้นเตียง", shift: "all", checked: true },
      { text: "ตรวจประเมินสภาพผิวหนังบริเวณปุ่มกระดูกสำคัญ (ก้นกบ, สะโพก, ส้นเท้า, หัวไหล่) ทุกเวร", shift: "all", checked: true },
      { text: "ดูแลผิวหนังให้สะอาดและแห้งอยู่เสมอ ไม่ให้เปียกชื้นจากเหงื่อหรือสิ่งขับถ่าย ทาโลชั่นบำรุงผิว", shift: "all", checked: true },
      { text: "ดูแลให้ได้รับสารอาหารและโปรตีนอย่างเพียงพอเพื่อส่งเสริมความแข็งแรงของชั้นผิวหนัง", shift: "morning", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ผิวหนังบริเวณก้นกบและส้นเท้าทั้งสองข้างสีปกติ ไม่มีรอยแดง พลิกตัวสม่ำเสมอทุก 2 ชม. ไม่พบแผลกดทับ"
  },
  {
    code: "00086",
    name: "เสี่ยงต่อระบบไหลเวียนโลหิตและเส้นประสาทส่วนปลายบกพร่อง (Risk for Peripheral Neurovascular Dysfunction)",
    domain: "4. Activity/Rest (กิจกรรมและการพักผ่อน)",
    category: "การเคลื่อนไหวและกระดูก",
    relatedTo: "กระดูกหัก, ผ่าตัดกระดูกและข้อ, ใส่เฝือก/Slab แน่น, มีอาการบวมของเนื้อเยื่อรอบรอยโรค",
    subjective: "ผู้ป่วยบอกปลายเท้าอุ่นดี กระดิกนิ้วได้ ไม่มีอาการชาหนาหรือปวดตึงแน่นรุนแรง",
    objective: "หลังผ่าตัดดามกระดูกขาขวา, สวม Elastic bandage / Slab, ปลายเท้าบวมเล็กน้อย, Capillary refill 1.5 วินาที",
    goal: "การไหลเวียนโลหิตส่วนปลาย (Tissue Perfusion: Peripheral: NOC 0407) - ปลายเท้าสีชมพู อุ่น ชีพจรเต้นชัดเจน, กระดิกนิ้วได้ดี ไม่ชา ไม่เกิด Compartment Syndrome",
    interventions: [
      { text: "ตรวจประเมิน CMS (Color, Motion, Sensation) และ Capillary refill time ปลายแขนขาข้างที่ผ่าตัดทุก 2-4 ชั่วโมง", shift: "all", checked: true },
      { text: "คลำตรวจ Peripheral pulse (Dorsalis pedis / Posterior tibial) เปรียบเทียบกับข้างปกติทุกเวร", shift: "all", checked: true },
      { text: "จัดยกอวัยวะข้างที่ผ่าตัดให้สูงกว่าระดับหัวใจ (Elevation) ด้วยหมอนหนุนเพื่อลดอาการบวมและส่งเสริมการไหลกลับของเลือดดำ", shift: "all", checked: true },
      { text: "เฝ้าระวังอาการแสดงของ Compartment Syndrome (5 Ps: Pain, Pallor, Pulselessness, Paresthesia, Paralysis) รายงานแพทย์ทันทีหากพบ", shift: "all", checked: true },
      { text: "ตรวจสอบผ้าพัน Elastic bandage หรือเฝือก ไม่ให้รัดแน่นจนเกินไป คลายออกทันทีหากพบอาการบวมแน่นตึง", shift: "all", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): CMS check ปลายเท้าขวา: สีผิวชมพู อุ่น Capillary refill 1.5 วินาที กระดิกนิ้วเท้าได้ดี ไม่มีอาการชา ชีพจรเต้นสม่ำเสมอ"
  },
  {
    code: "00031",
    name: "การกำจัดเสมหะไม่มีประสิทธิภาพ (Ineffective Airway Clearance)",
    domain: "11. Safety/Protection (ความปลอดภัย)",
    category: "ระบบทางเดินหายใจ",
    relatedTo: "มีเสมหะเหนียวข้นในทางเดินหายใจ, ไอไม่แรงจากความปวดแผลผ่าตัด, ฤทธิ์ยาดมสลบ",
    subjective: "ผู้ป่วยบ่นเหนื่อย ไอไม่ออก มีเสมหะติดค้างในลำคอ",
    objective: "ฟังปอดได้ยินเสียง Rhonchi ชายปอดทั้งสองข้าง, หายใจตื้น, อัตราการหายใจ 22 ครั้ง/นาที, SpO2 95% room air",
    goal: "ทางเดินหายใจโล่ง (Respiratory Status: Airway Patency: NOC 0410) - ทางเดินหายใจโล่ง ไม่มีเสียงเสมหะคั่ง, ขับเสมหะได้ดี, SpO2 ≥ 96%",
    interventions: [
      { text: "จัดท่านอนศีรษะสูง 30-45 องศา (Semi-Fowler's position) เพื่อให้กะบังลมหย่อนตัวและปอดขยายตัวได้เต็มที่", shift: "all", checked: true },
      { text: "สอนและกระตุ้นการไออย่างมีประสิทธิภาพ (Effective coughing) โดยใช้หมอนกอดประคองแผลผ่าตัด (Splinting) ขณะไอ", shift: "morning", checked: true },
      { text: "ส่งเสริมและกระตุ้นการดูดเครื่องบริหารปอด (Triflow / Incentive spirometry) 10 ครั้ง/ชั่วโมง ขณะตื่น", shift: "morning", checked: true },
      { text: "ดูแลให้ได้รับออกซิเจนตามแผนการรักษา และวัดประเมินความอิ่มตัวของออกซิเจน (SpO2) อย่างต่อเนื่อง", shift: "all", checked: true },
      { text: "กระตุ้นให้ดื่มน้ำอุ่นอย่างเพียงพอเพื่อช่วยละลายเสมหะให้ขับออกได้ง่ายขึ้น (หากไม่มีข้อจำกัด)", shift: "afternoon", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ผู้ป่วยสามารถดูด Triflow ได้ 3 ลูก และไอขับเสมหะสีขาวขุ่นออกมาได้ ฟังปอดเสียงโปร่งขึ้น SpO2 98% on room air"
  },
  {
    code: "00027",
    name: "การขาดสมดุลสารน้ำในร่างกาย / ปริมาตรสารน้ำพร่อง (Deficient Fluid Volume)",
    domain: "2. Nutrition (ภาวะโภชนาการและสารน้ำ)",
    category: "สารน้ำและเกลือแร่",
    relatedTo: "เสียเลือดจากการผ่าตัด / เสียสารน้ำทางสายระบาย / งดน้ำและอาหาร (NPO) / รับประทานได้น้อย",
    subjective: "ผู้ป่วยบ่นกระหายน้ำ คอแห้ง อ่อนเพลีย",
    objective: "ริมฝีปากแห้ง, ชีพจรเร็ว 98 bpm, BP 102/65 mmHg, เลือดออกทางสายระบาย 150 ml, ปัสสาวะออกน้อย",
    goal: "สมดุลสารน้ำ (Fluid Balance: NOC 0601) - สัญญาณชีพคงที่, ค่า I/O สมดุล, ปัสสาวะออก > 0.5 ml/kg/hr (> 30 ml/hr), ผิวหนังมีความตึงตัวดี",
    interventions: [
      { text: "บันทึกปริมาณสารน้ำเข้าและออกจากร่างกายอย่างละเอียด (Intake & Output) ทุก 8 ชั่วโมง", shift: "all", checked: true },
      { text: "ดูแลให้สารน้ำทางหลอดเลือดดำ (IV Infusion) ตามแผนการรักษา และควบคุมอัตราการหยดอย่างเคร่งครัด", shift: "all", checked: true },
      { text: "ติดตามสัญญาณชีพและระดับความรู้สึกตัวทุก 2-4 ชั่วโมง รายงานแพทย์หาก BP ต่ำกว่า 90/60 mmHg หรือชีพจร > 110 bpm", shift: "all", checked: true },
      { text: "ติดตามประเมินปริมาณเลือดที่ออกจากสายระบาย (Drain content) บันทึกสีและลักษณะ", shift: "all", checked: true },
      { text: "ตรวจติดตามผลตรวจทางห้องปฏิบัติการ: Hct, Electrolytes, BUN, Cr และรายงานแพทย์ทันทีเมื่อพบค่าวิกฤต", shift: "morning", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): สารน้ำ IV หยดสม่ำเสมอตามแผนการรักษา ปัสสาวะออก 350 ml/8 ชม. สีเหลืองใส BP 115/72 mmHg, PR 80 bpm ไม่มีอาการหน้ามืด"
  },
  {
    code: "00016",
    name: "การขับถ่ายปัสสาวะบกพร่อง / ปัสสาวะคั่ง (Impaired Urinary Elimination / Retention)",
    domain: "3. Elimination and Exchange (การขับถ่าย)",
    category: "ระบบขับถ่าย",
    relatedTo: "ฤทธิ์ของยาระงับความรู้สึกทางไขสันหลัง (Spinal block) / คาสายสวนปัสสาวะ / ปวดแผล",
    subjective: "ผู้ป่วยบอกปวดตึงท้องน้อย ปัสสาวะไม่ออกหลังถอดสายสวนปัสสาวะ",
    objective: "คลำพบกระเพาะปัสสาวะโป่งตึงเหนือหัวเหน่า (Bladder distension), ถอด Foley catheter มาแล้ว 6 ชั่วโมง",
    goal: "การขับถ่ายปัสสาวะ (Urinary Elimination: NOC 0503) - สามารถขับถ่ายปัสสาวะได้เองภายใน 6-8 ชั่วโมงหลังถอดสายสวน, ปัสสาวะออกคล่อง ไม่มีปัสสาวะคั่งค้าง",
    interventions: [
      { text: "ดูแลความสะอาดและการระบายของสายสวนปัสสาวะ Foley catheter ให้ไหลสะดวก ไม่หักพับงอ ถุงอยู่ต่ำกว่ากระเพาะปัสสาวะ", shift: "all", checked: true },
      { text: "ภายหลังถอดสายสวนปัสสาวะ: กระตุ้นให้ผู้ป่วยลุกปัสสาวะภายใน 6-8 ชั่วโมง", shift: "morning", checked: true },
      { text: "ตรวจคลำบริเวณท้องน้อยเพื่อประเมินภาวะกระเพาะปัสสาวะโป่งตึง (Bladder distension)", shift: "all", checked: true },
      { text: "แนะนำเทคนิคกระตุ้นการขับถ่ายปัสสาวะ เช่น การเปิดน้ำไหล, การประคบอุ่นบริเวณท้องน้อย, การลุกนั่งปัสสาวะในท่าปกติ", shift: "afternoon", checked: true },
      { text: "ปรึกษาแพทย์เพื่อทำการสวนปัสสาวะทิ้ง (In-and-out catheterization) หากไม่สามารถปัสสาวะได้เองเกิน 8 ชั่วโมง", shift: "all", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ผู้ป่วยสามารถปัสสาวะได้เองหลังถอดสายสวน ปัสสาวะออก 400 ml สีเหลืองใส ไม่ปวดตึงท้องน้อย"
  },
  {
    code: "00011",
    name: "ท้องผูก / เสี่ยงต่อท้องผูก (Constipation / Risk for Constipation)",
    domain: "3. Elimination and Exchange (การขับถ่าย)",
    category: "ระบบขับถ่าย",
    relatedTo: "การนอนนิ่งบนเตียงลดการเคลื่อนไหว / ผลข้างเคียงของยาแก้ปวดกลุ่ม Opioids / รับประทานอาหารและน้ำลดลง",
    subjective: "ผู้ป่วยบอกไม่ได้ถ่ายอุจจาระมา 3 วัน รู้สึกอึดอัดแน่นท้อง",
    objective: "ท้องอืดเล็กน้อย ฟัง Bowel sound ได้ยินเบาลง (3-4 ครั้ง/นาที), ได้รับยา Morphine/Tramadol บรรเทาปวด",
    goal: "การขับถ่ายอุจจาระ (Bowel Elimination: NOC 0501) - ขับถ่ายอุจจาระนิ่มอย่างน้อยวันเว้นวัน สบายท้อง ไม่ต้องออกแรงเบ่งรุนแรง",
    interventions: [
      { text: "ประเมินและบันทึกประวัติการขับถ่ายอุจจาระ ความถี่ ลักษณะ และอาการแน่นอึดอัดท้องทุกเวร", shift: "all", checked: true },
      { text: "ฟังเสียงการเคลื่อนไหวของลำไส้ (Bowel sound) ทุกเวร", shift: "all", checked: true },
      { text: "แนะนำให้ดื่มน้ำอุ่นอย่างน้อย 2,000 ml/วัน (หากไม่มีข้อห้าม) และรับประทานอาหารที่มีกากใยสูง", shift: "morning", checked: true },
      { text: "ส่งเสริมการเคลื่อนไหวร่างกาย การพลิกตัว และการลุกเดินเท่าที่ผู้ป่วยทนได้เพื่อกระตุ้นการบีบตัวของลำไส้", shift: "morning", checked: true },
      { text: "ดูแลให้ยาระบาย (Laxatives) หรือเหน็บยาตามแผนการรักษาของแพทย์", shift: "night", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ได้รับยาระบายตามแผนการรักษา ผู้ป่วยถ่ายอุจจาระนิ่มสีน้ำตาล 1 ครั้ง ท้องยุบลง สบายตัวขึ้น"
  },
  {
    code: "00146",
    name: "ความวิตกกังวล (Anxiety)",
    domain: "9. Coping/Stress Tolerance (การเผชิญความเครียด)",
    category: "จิตสังคมและความรู้",
    relatedTo: "ความกลัวการผ่าตัด / ความกลัวความพิการ / สภาพแวดล้อมโรงพยาบาลที่ไม่คุ้นเคย",
    subjective: "ผู้ป่วยบ่นกังวล กลัวการผ่าตัด กลัวไม่ฟื้น กลัวเดินไม่ได้เหมือนเดิม",
    objective: "สีหน้ากระวนกระวาย มือเย็น เหงื่อซึม นอนไม่หลับ ชีพจร 96 bpm",
    goal: "การควบคุมความวิตกกังวล (Anxiety Level: NOC 1402) - ผู้ป่วยมีสีหน้าสงบลง สามารถบอกความรู้สึกและผ่อนคลายความวิตกกังวลได้ สัญญาณชีพคงที่",
    interventions: [
      { text: "สร้างสัมพันธภาพที่ดี รับฟังความรู้สึกและความกังวลของผู้ป่วยด้วยความเข้าใจและไม่ตัดสิน", shift: "all", checked: true },
      { text: "อธิบายขั้นตอนการผ่าตัด การให้ยาระงับความรู้สึก และการดูแลหลังผ่าตัดด้วยคำพูดที่เข้าใจง่าย", shift: "morning", checked: true },
      { text: "เปิดโอกาสให้ผู้ป่วยและญาติซักถามข้อสงสัยและให้ข้อมูลที่ถูกต้องตามความเป็นจริง", shift: "all", checked: true },
      { text: "แนะนำและฝึกเทคนิคการผ่อนคลาย (Deep breathing, Guided imagery, ดนตรีบำบัด)", shift: "afternoon", checked: true },
      { text: "จัดสิ่งแวดล้อมให้เงียบสงบ ลดเสียงรบกวนเพื่อให้ผู้ป่วยได้นอนหลับพักผ่อนอย่างเต็มที่", shift: "night", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ผู้ป่วยมีสีหน้ายิ้มแย้มขึ้น บอกว่าเข้าใจขั้นตอนการรักษาและคลายความกังวลลง นอนหลับพักผ่อนได้ดี"
  },
  {
    code: "00126",
    name: "พร่องความรู้ในการดูแลตนเอง (Deficient Knowledge)",
    domain: "5. Perception/Cognition (การรับรู้และความรู้)",
    category: "จิตสังคมและความรู้",
    relatedTo: "ขาดประสบการณ์การผ่าตัด / ไม่คุ้นเคยกับการดูแลแผลและข้อจำกัดการเคลื่อนไหวที่บ้าน",
    subjective: "ผู้ป่วยและญาติถามบ่อยเรื่องกลับบ้านทำอะไรได้บ้าง ไม่แน่ใจเรื่องการกินยาและการทำแผล",
    objective: "ผู้ป่วยกำลังจะจำหน่ายออกจากโรงพยาบาล (D/C), มีแผลผ่าตัดและอุปกรณ์ช่วยพยุง",
    goal: "ความรู้: ขั้นตอนและการดูแลหลังการรักษา (Knowledge: Treatment Regimen: NOC 1813) - ผู้ป่วยและญาติอธิบายและสาธิตการดูแลตนเอง การกินยา และข้อห้ามได้ถูกต้อง",
    interventions: [
      { text: "ให้คำแนะนำและแจกเอกสารคู่มือการปฏิบัติตัวหลังผ่าตัด (การดูแลแผล, การหลีกเลี่ยงการโดนน้ำ)", shift: "morning", checked: true },
      { text: "แนะนำข้อห้ามเฉพาะโรค เช่น ท่าทางที่ห้ามทำหลังผ่าตัดเปลี่ยนข้อสะโพกเทียม (ห้ามงอสะโพก > 90 องศา, ห้ามนั่งไขว่ห้าง)", shift: "morning", checked: true },
      { text: "อธิบายการรับประทานยาอย่างต่อเนื่องตามแพทย์สั่ง และเน้นย้ำอาการผิดปกติที่ต้องมาพบแพทย์ทันทีก่อนวันนัด", shift: "morning", checked: true },
      { text: "เปิดโอกาสให้ผู้ป่วยและญาติได้ฝึกสาธิตย้อนกลับ (Teach-back method)", shift: "afternoon", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ผู้ป่วยและญาติสามารถบอกข้อห้ามหลังผ่าตัดและสาธิตการบริหารกล้ามเนื้อได้อย่างถูกต้อง พร้อมกลับบ้าน"
  },
  {
    code: "00095",
    name: "แบบแผนการนอนหลับแปรปรวน (Disturbed Sleep Pattern)",
    domain: "4. Activity/Rest (กิจกรรมและการพักผ่อน)",
    category: "ความปวดและการบรรเทา",
    relatedTo: "ความปวดแผล / เสียงรบกวนในหอผู้ป่วย / การวัดสัญญาณชีพในเวลากลางคืน / แสงไฟ",
    subjective: "ผู้ป่วยบ่นนอนไม่หลับ หลับๆ ตื่นๆ ทั้งคืน ตื่นมาแล้วรู้สึกเพลีย",
    objective: "ตาโรย อ่อนล้า หาวบ่อย มีอาการง่วงซึมในเวลากลางวัน",
    goal: "การนอนหลับ (Sleep: NOC 0004) - นอนหลับได้ต่อเนื่องอย่างน้อย 6-8 ชั่วโมงในเวลากลางคืน ตื่นมาสดชื่น ไม่อ่อนเพลีย",
    interventions: [
      { text: "จัดกิจกรรมการพยาบาลและการให้ยาให้รวมเป็นช่วงเวลาเดียวกัน (Clustering care) เพื่อลดการรบกวนผู้ป่วยในเวลากลางคืน", shift: "night", checked: true },
      { text: "ปรับหรี่แสงไฟในห้องพักผู้ป่วย ปิดประตู และควบคุมระดับเสียงรบกวนจากอุปกรณ์และบุคลากร", shift: "night", checked: true },
      { text: "จัดท่านอนที่สบาย หนุนหมอนประคองอวัยวะข้างที่ผ่าตัดเพื่อลดอาการปวดรบกวนการนอน", shift: "night", checked: true },
      { text: "ให้ยาบรรเทาปวดก่อนเวลานอนตามแผนการรักษาเพื่อให้ผู้ป่วยพักผ่อนได้อย่างสุขสบาย", shift: "night", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): จัดสิ่งแวดล้อมเงียบสงบ ผู้ป่วยนอนหลับได้ต่อเนื่องตั้งแต่ 23:00 - 05:30 น. ตื่นมาสดชื่นดี"
  },
  {
    code: "00134",
    name: "คลื่นไส้ อาเจียน (Nausea / Vomiting)",
    domain: "12. Comfort (ความสุขสบาย)",
    category: "ความปวดและการบรรเทา",
    relatedTo: "ฤทธิ์ของยาดมสลบ / ยาแก้ปวดกลุ่ม Opioids / การระคายเคืองของเยื่อบุกระเพาะอาหาร",
    subjective: "ผู้ป่วยบ่นพะอืดพะอม คลื่นไส้ อยากอาเจียน",
    objective: "สีหน้าซีด มีอาการขย้อน อาเจียนเป็นเศษอาหาร/น้ำลาย 100 ml",
    goal: "การควบคุมอาการคลื่นไส้อาเจียน (Nausea & Vomiting Severity: NOC 2106) - อาการคลื่นไส้ลดลง ไม่มีอาการอาเจียน สามารถจิบน้ำ/รับประทานอาหารเหลวได้",
    interventions: [
      { text: "ประเมินความถี่ ความรุนแรงของอาการคลื่นไส้ และปริมาณลักษณะของสิ่งขับถ่ายที่อาเจียน", shift: "all", checked: true },
      { text: "ให้การดูแลและช่วยเหลือขณะอาเจียน จัดให้นอนตะแคงหน้าเพื่อป้องกันการสำลัก (Aspiration prevention)", shift: "all", checked: true },
      { text: "ดูแลบ้วนปากทำความสะอาดช่องปากหลังอาเจียนเพื่อลดกลิ่นและรสชาติไม่พึงประสงค์", shift: "all", checked: true },
      { text: "ให้ยาแก้อาเจียน (Antiemetic) เช่น Ondansetron / Dimenhydrinate ตามแผนการรักษาของแพทย์", shift: "all", checked: true },
      { text: "แนะนำให้จิบน้ำอุ่นทีละน้อย และหลีกเลี่ยงอาหารที่มีกลิ่นฉุนหรือมันเยิ้ม", shift: "morning", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): หลังได้รับยาแก้อาเจียน 30 นาที ผู้ป่วยบอกอาการคลื่นไส้ทุเลาลง สามารถจิบน้ำอุ่นได้ ไม่มีการอาเจียนซ้ำ"
  },
  {
    code: "00032",
    name: "แบบแผนการหายใจไม่มีประสิทธิภาพ (Ineffective Breathing Pattern)",
    domain: "4. Activity/Rest (กิจกรรมและการพักผ่อน)",
    category: "ระบบทางเดินหายใจ",
    relatedTo: "ความปวดแผลบริเวณทรวงอก/หน้าท้อง / กล้ามเนื้อช่วยหายใจอ่อนแรง / ผลข้างเคียงยาระงับประสาท",
    subjective: "หายใจตื้น หายใจเหนื่อย กลัวปวดเวลาหายใจลึก",
    objective: "อัตราการหายใจเร็ว 24 ครั้ง/นาที หายใจตื้น ใช้กล้ามเนื้อหน้าอกช่วยหายใจ SpO2 93%",
    goal: "สถานะการหายใจ (Respiratory Status: NOC 0415) - อัตราและจังหวะการหายใจสม่ำเสมอ (RR 16-20 bpm), ไม่ใช้กล้ามเนื้อช่วยหายใจ, SpO2 ≥ 96%",
    interventions: [
      { text: "จัดท่านอนศีรษะสูง 45-60 องศา เพื่อเพิ่มปริมาตรช่องอกให้ปอดขยายตัวได้เต็มที่", shift: "all", checked: true },
      { text: "ให้การดูแลระงับความปวดให้เพียงพอก่อนฝึกการหายใจลึก เพื่อให้ผู้ป่วยหายใจได้เต็มที่โดยไม่เจ็บแผล", shift: "all", checked: true },
      { text: "สอนการหายใจลึกช้าๆ (Pursed-lip breathing & Deep breathing exercises)", shift: "morning", checked: true },
      { text: "ให้ออกซิเจนแคนนูลาตามแผนการรักษา และวัด SpO2 ทุก 2-4 ชั่วโมง", shift: "all", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): จัดท่านอนศีรษะสูง ผู้ป่วยหายใจลึกได้ดีขึ้น อัตราการหายใจ 18 ครั้ง/นาที สม่ำเสมอ SpO2 99% on O2 cannula 3 LPM"
  },
  {
    code: "00206",
    name: "เสี่ยงต่อภาวะเลือดออก (Risk for Bleeding)",
    domain: "11. Safety/Protection (ความปลอดภัย)",
    category: "ความปลอดภัยของผู้ป่วย",
    relatedTo: "แผลผ่าตัดใหญ่, ได้รับยาต้านการแข็งตัวของเลือด (Anticoagulants / LMWH / Warfarin), เกล็ดเลือดต่ำ",
    subjective: "ผู้ป่วยบอกไม่มีอาการหน้ามืด วิงเวียน หรือใจสั่น",
    objective: "ผ่าตัดเปลี่ยนข้อเทียม, ได้รับยา Clexane ฉีดใต้ผิวหนัง, มีสายระบายเลือด",
    goal: "การควบคุมการสูญเสียเลือด (Blood Loss Severity: NOC 0413) - ไม่มีภาวะเลือดออกผิดปกติ, เลือดในสายระบายลดลงเรื่อยๆ, Hct/Hb คงที่",
    interventions: [
      { text: "สังเกตและตรวจดูผ้าปิดแผลผ่าตัดว่ามีเลือดสดซึมเปื้อนหรือไม่ บันทึกขนาดรอยซึม", shift: "all", checked: true },
      { text: "ตรวจวัดปริมาณเลือดในสายระบายสุญญากาศทุก 4-8 ชั่วโมง รายงานแพทย์ทันทีหากออก > 100 ml/hr ติดต่อกัน 2 ชม.", shift: "all", checked: true },
      { text: "สังเกตอาการแสดงของภาวะเลือดออกภายใน: ความดันโลหิตตก, ชีพจรเต้นเร็วและเบา, เหงื่อแตก ตัวเย็น, หน้ามืด", shift: "all", checked: true },
      { text: "ติดตามผลแล็บ CBC (Hb, Hct, Platelet count) และ Coagulogram (PT, PTT, INR)", shift: "morning", checked: true },
      { text: "หลีกเลี่ยงกิจกรรมที่อาจก่อให้เกิดการบาดเจ็บหรือแผลช้ำ", shift: "all", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): แผลผ่าตัดไม่มีเลือดซึม เลือดออกในสายระบาย 40 ml/8 ชม. สีแดงจาง สัญญาณชีพคงที่ BP 120/75 mmHg, PR 76 bpm"
  },
  {
    code: "00173",
    name: "เสี่ยงต่อความสับสนเฉียบพลัน (Risk for Acute Confusion / Delirium)",
    domain: "5. Perception/Cognition (การรับรู้และความรู้)",
    category: "จิตสังคมและความรู้",
    relatedTo: "ผู้สูงอายุหลังผ่าตัด, ได้รับยาระงับความรู้สึก, ความไม่สมดุลของเกลือแร่, นอนไม่หลับ",
    subjective: "ญาติบอกผู้ป่วยจำชื่อลูกหลานได้ แต่ช่วงค่ำๆ เริ่มถามซ้ำๆ ว่าอยู่ที่ไหน",
    objective: "อายุ > 70 ปี, หลังผ่าตัดกระดูกสะโพกหัก, มีประวัติสับสนช่วงกลางคืน (Sundowning)",
    goal: "ความสามารถในการรับรู้และสติสัมปชัญญะ (Cognition: NOC 0900) - รู้สติ รู้กาลเวลา สถานที่ และบุคคล (Oriented to time, place, person)",
    interventions: [
      { text: "ประเมินระดับความรู้สึกตัวและการรับรู้สติสัมปชัญญะ (Orientation to time, place, person) ทุกเวร", shift: "all", checked: true },
      { text: "พูดคุยบอกวัน เวลา สถานที่ และบุคคลให้ผู้ป่วยรับทราบเป็นระยะ (Re-orientation)", shift: "morning", checked: true },
      { text: "จัดให้มีแสงสว่างในเวลากลางวัน และลดแสงสว่างในเวลากลางคืนเพื่อคงวงจรการตื่น-หลับปกติ", shift: "all", checked: true },
      { text: "สนับสนุนให้มีญาติหรือผู้ดูแลที่คุ้นเคยอยู่ข้างเตียงเพื่อช่วยสร้างความอบอุ่นใจ", shift: "all", checked: true },
      { text: "ตรวจสอบความปลอดภัย จัดของมีคมและสายระบายให้อยู่ในตำแหน่งที่ปลอดภัย ไม่ดึงรั้ง", shift: "all", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ผู้ป่วยตื่นดี พูดคุยตอบคำถามรู้เรื่อง จำญาติและพยาบาลได้ ไม่มีอาการเอะอะโวยวายหรือดึงสายน้ำเกลือ"
  },
  {
    code: "00195",
    name: "เสี่ยงต่อความไม่สมดุลของเกลือแร่ในร่างกาย (Risk for Electrolyte Imbalance)",
    domain: "2. Nutrition (ภาวะโภชนาการและสารน้ำ)",
    category: "สารน้ำและเกลือแร่",
    relatedTo: "ได้รับยาขับปัสสาวะ, เสียสารน้ำทางสายระบาย, งดอาหารนาน, โรคไตเรื้อรัง, ท้องเสีย/อาเจียน",
    subjective: "ผู้ป่วยบอกไม่มีอาการใจสั่น ไม่มีอาการชาตามปลายมือปลายเท้า",
    objective: "ผล K = 3.2 mEq/L (Hypokalemia), Na = 133 mEq/L, มีสายระบายทางเดินอาหาร",
    goal: "สมดุลของอิเล็กโทรไลต์และกรดด่าง (Electrolyte & Acid/Base Balance: NOC 0606) - ค่าผลเลือด Na, K, Cl, Ca อยู่ในเกณฑ์ปกติ ไม่มีอาการกล้ามเนื้ออ่อนแรงหรือหัวใจเต้นผิดจังหวะ",
    interventions: [
      { text: "เฝ้าระวังอาการแสดงของเกลือแร่ผิดปกติ เช่น กล้ามเนื้อกระตุก อ่อนแรง ชา หัวใจเต้นผิดจังหวะ ท้องอืด", shift: "all", checked: true },
      { text: "ดูแลให้ได้รับเกลือแร่ทดแทนทางหลอดเลือดดำ (เช่น KCL in IV fluid) ตามแผนการรักษาของแพทย์อย่างเคร่งครัด", shift: "all", checked: true },
      { text: "ตรวจสอบอัตราการหยดของ IV ไม่ให้เร็วเกินขนาดที่กำหนด เพื่อความปลอดภัยของหัวใจ", shift: "all", checked: true },
      { text: "ตรวจติดตามผลตรวจระดับเกลือแร่ในเลือดซ้ำตามแผนการรักษา", shift: "morning", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): สารน้ำผสม KCL หยดตามแผนการรักษา ผู้ป่วยไม่มีอาการกล้ามเนื้ออ่อนแรง สัญญาณชีพปกติ ติดตามผลเลือดซ้ำตามนัด"
  },
  {
    code: "00026",
    name: "ภาวะปริมาตรสารน้ำคั่งเกิน (Excess Fluid Volume)",
    domain: "2. Nutrition (ภาวะโภชนาการและสารน้ำ)",
    category: "สารน้ำและเกลือแร่",
    relatedTo: "การทำงานของหัวใจหรือไตลดลง / ได้รับสารน้ำทางหลอดเลือดดำปริมาณมากเกิน",
    subjective: "ผู้ป่วยบ่นแน่นอึดอัด หายใจไม่เต็มอิ่มเวลาเอนตัวนอนราบ",
    objective: "มีอาการบวมกดบุ๋ม (Pitting edema 2+) บริเวณขาทั้งสองข้าง, หายใจเหนื่อยหอบ, ฟังปอดได้ยิน Crepitation ชายปอด, I/O positive +1,200 ml",
    goal: "ภาวะสารน้ำเกิน (Fluid Overload Severity: NOC 0603) - ไม่มีอาการบวมกดบุ๋ม, ฟังปอดไม่มีเสียง Crepitation, หายใจปกติ SpO2 ≥ 96%",
    interventions: [
      { text: "ประเมินอาการบวมกดบุ๋ม (Pitting edema) และฟังเสียงปอดทุกเวร", shift: "all", checked: true },
      { text: "บันทึกปริมาณน้ำเข้า-ออก (I/O) อย่างเคร่งครัด ชั่งน้ำหนักตัวทุกเช้า", shift: "all", checked: true },
      { text: "ควบคุมปริมาณการดื่มน้ำและจำกัดอัตราการให้สารน้ำทาง IV ตามคำสั่งแพทย์อย่างเข้มงวด", shift: "all", checked: true },
      { text: "ดูแลให้ยาขับปัสสาวะ (Diuretic) ตามแผนการรักษา และสังเกตปริมาณปัสสาวะที่ออก", shift: "morning", checked: true },
      { text: "จัดท่านอนศีรษะสูง 45 องศาเพื่อลดแรงต้านในปอดและช่วยให้หายใจได้สะดวกขึ้น", shift: "all", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): ได้รับยา Lasix 40 mg IV ปัสสาวะออก 800 ml ใน 2 ชม. อาการเหนื่อยลดลง ฟังปอดเสียงใสขึ้น SpO2 98% on room air"
  },
  {
    code: "00214",
    name: "ความไม่สุขสบายในผู้ป่วยระยะประคับประคอง (Impaired Comfort / Palliative Care)",
    domain: "12. Comfort (ความสุขสบาย)",
    category: "ความปวดและการบรรเทา",
    relatedTo: "โรคมะเร็งระยะลุกลาม / ความเจ็บปวดจากพยาธิสภาพของโรค / หายใจลำบาก / อ่อนล้า",
    subjective: "ผู้ป่วยบ่นเหนื่อย อ่อนเพลีย ปวดกระดูก ไม่สบายตัว อยากพักผ่อน",
    objective: "นอนบนเตียง รูปร่างซูบผอม หายใจเหนื่อย มีอาการกระสับกระส่าย",
    goal: "สถานะความสุขสบาย (Comfort Status: NOC 2008) - ผู้ป่วยมีความสุขสบาย สีหน้าสงบ ปราศจากความเจ็บปวดรุนแรง หายใจราบเรียบขึ้น",
    interventions: [
      { text: "ประเมินอาการรบกวนความสุขสบาย (Pain, Dyspnea, Fatigue, Nausea) อย่างสม่ำเสมอ", shift: "all", checked: true },
      { text: "ให้ยาบรรเทาปวดและยาขยายหลอดลม/ลดอาการหอบเหนื่อย (Morphine / Bronchodilator) ตามแผนการรักษา", shift: "all", checked: true },
      { text: "เช็ดตัว ดูแลสุขวิทยาส่วนบุคคล พลิกตัวอย่างนุ่มนวล และประคองด้วยหมอนหนุนเพื่อความสุขสบาย", shift: "all", checked: true },
      { text: "เปิดโอกาสและสนับสนุนให้ครอบครัวได้อยู่ดูแลเคียงข้างอย่างใกล้ชิด", shift: "all", checked: true },
      { text: "จัดสิ่งแวดล้อมให้สงบ อากาศถ่ายเทสะดวก และเปิดดนตรี/สวดมนต์ตามความเชื่อของผู้ป่วย", shift: "night", checked: true }
    ],
    response: "ประเมินผลลัพธ์ (NOC Evaluation): หลังให้การพยาบาล ผู้ป่วยนอนหลับพักผ่อนได้ สีหน้าสงบ ลมหายใจสม่ำเสมอ ญาติได้อยู่ดูแลข้างเตียงด้วยความสบายใจ"
  }
];

let activeNicNocItem = null;
let currentNicNocShift = "all"; // "all", "morning", "afternoon", "night"
let activeNicNocTargetBed = 1;

function initNicNocModule() {
  const openBtn = document.getElementById('openNicNocModalBtn');
  if (openBtn) {
    openBtn.addEventListener('click', () => openNicNocModal());
  }

  const pickerBtn = document.getElementById('btnOpenNicNocPicker');
  if (pickerBtn) {
    pickerBtn.addEventListener('click', () => {
      openNicNocModal(typeof activeBedNumber !== 'undefined' ? activeBedNumber : 1);
    });
  }

  const closeBtn = document.getElementById('nicNocModalCloseBtn');
  if (closeBtn) {
    closeBtn.addEventListener('click', closeNicNocModal);
  }

  const searchInput = document.getElementById('nicNocSearchInput');
  if (searchInput) {
    searchInput.addEventListener('input', () => filterNicNocDiagnoses());
  }

  const clearBtn = document.getElementById('clearNicNocSearchBtn');
  if (clearBtn) {
    clearBtn.addEventListener('click', () => {
      if (searchInput) {
        searchInput.value = '';
        searchInput.focus();
        filterNicNocDiagnoses();
      }
    });
  }

  // Shift pills
  const shiftBtns = document.querySelectorAll('.nicnoc-shift-btn');
  shiftBtns.forEach(btn => {
    btn.addEventListener('click', (e) => {
      shiftBtns.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      currentNicNocShift = btn.getAttribute('data-shift') || 'all';
      renderNicNocActivities();
      updateNicNocDarPreview();
    });
  });

  // Action buttons
  const copyBtn = document.getElementById('btnCopyNicNocDar');
  if (copyBtn) {
    copyBtn.addEventListener('click', copyNicNocDar);
  }

  const insertBtn = document.getElementById('btnInsertNicNocToBed');
  if (insertBtn) {
    insertBtn.addEventListener('click', insertNicNocToActiveBed);
  }

  const selectAllBtn = document.getElementById('btnNicNocSelectAll');
  if (selectAllBtn) {
    selectAllBtn.addEventListener('click', () => {
      if (!activeNicNocItem) return;
      activeNicNocItem.interventions.forEach(a => {
        if (currentNicNocShift === 'all' || a.shift === 'all' || a.shift === currentNicNocShift) {
          a.checked = true;
        }
      });
      renderNicNocActivities();
      updateNicNocDarPreview();
    });
  }

  const clearAllBtn = document.getElementById('btnNicNocClearAll');
  if (clearAllBtn) {
    clearAllBtn.addEventListener('click', () => {
      if (!activeNicNocItem) return;
      activeNicNocItem.interventions.forEach(a => {
        if (currentNicNocShift === 'all' || a.shift === 'all' || a.shift === currentNicNocShift) {
          a.checked = false;
        }
      });
      renderNicNocActivities();
      updateNicNocDarPreview();
    });
  }

  // Dynamic preview update on text input change
  ['nicNocTxtSubj', 'nicNocTxtObj', 'nicNocTxtGoal', 'nicNocTxtResp'].forEach(id => {
    const el = document.getElementById(id);
    if (el) {
      el.addEventListener('input', () => updateNicNocDarPreview());
    }
  });

  // Render initial list
  renderNicNocCategoryPills();
  filterNicNocDiagnoses();
}

function openNicNocModal(targetBed = -1) {
  const modal = document.getElementById('nicNocModal');
  if (!modal) return;

  // Determine target bed
  if (targetBed > 0 && targetBed <= 30) {
    activeNicNocTargetBed = targetBed;
  } else if (typeof activeBedNumber !== 'undefined' && activeBedNumber > 0) {
    activeNicNocTargetBed = activeBedNumber;
  } else {
    activeNicNocTargetBed = 1;
  }

  const bedSelect = document.getElementById('nicNocBedSelect');
  if (bedSelect) {
    bedSelect.innerHTML = '';
    for (let i = 1; i <= 30; i++) {
      const opt = document.createElement('option');
      opt.value = i;
      opt.textContent = `เตียง ${String(i).padStart(2, '0')}`;
      if (i === activeNicNocTargetBed) opt.selected = true;
      bedSelect.appendChild(opt);
    }
    bedSelect.onchange = (e) => {
      activeNicNocTargetBed = parseInt(e.target.value, 10) || 1;
      updateNicNocActionButtons();
    };
  }

  updateNicNocActionButtons();

  modal.classList.add('open');
  modal.setAttribute('aria-hidden', 'false');

  if (!activeNicNocItem && NIC_NOC_CATALOG.length > 0) {
    selectNicNocDiagnosis(NIC_NOC_CATALOG[0].code);
  }
}

function closeNicNocModal() {
  const modal = document.getElementById('nicNocModal');
  if (modal) {
    modal.classList.remove('open');
    modal.setAttribute('aria-hidden', 'true');
  }
}

function updateNicNocActionButtons() {
  const insertBtn = document.getElementById('btnInsertNicNocToBed');
  if (insertBtn) {
    insertBtn.innerHTML = `<i class="fa-solid fa-bed"></i> แทรกลงเตียง ${String(activeNicNocTargetBed).padStart(2, '0')}`;
  }
}

function renderNicNocCategoryPills() {
  const container = document.getElementById('nicNocCategoryPills');
  if (!container) return;

  const categories = ["ทั้งหมด", "ความปวดและการบรรเทา", "การเคลื่อนไหวและกระดูก", "ความปลอดภัยของผู้ป่วย", "การติดเชื้อและการหายของแผล", "ผิวหนังและแผลกดทับ", "ระบบทางเดินหายใจ", "สารน้ำและเกลือแร่", "ระบบขับถ่าย", "จิตสังคมและความรู้"];
  container.innerHTML = '';

  categories.forEach((cat, index) => {
    const pill = document.createElement('button');
    pill.type = 'button';
    pill.className = `template-cat-pill ${index === 0 ? 'active' : ''}`;
    pill.textContent = cat;
    pill.onclick = () => {
      container.querySelectorAll('.template-cat-pill').forEach(p => p.classList.remove('active'));
      pill.classList.add('active');
      filterNicNocDiagnoses();
    };
    container.appendChild(pill);
  });
}

function filterNicNocDiagnoses() {
  const searchInput = document.getElementById('nicNocSearchInput');
  const term = searchInput ? searchInput.value.trim().toLowerCase() : '';
  const activePill = document.querySelector('#nicNocCategoryPills .template-cat-pill.active');
  const selectedCat = activePill ? activePill.textContent : 'ทั้งหมด';

  const listPane = document.getElementById('nicNocListPane');
  if (!listPane) return;

  listPane.innerHTML = '';

  const filtered = NIC_NOC_CATALOG.filter(item => {
    const matchCat = (selectedCat === 'ทั้งหมด' || item.category === selectedCat);
    const matchTerm = !term ||
      item.code.toLowerCase().includes(term) ||
      item.name.toLowerCase().includes(term) ||
      item.domain.toLowerCase().includes(term) ||
      item.relatedTo.toLowerCase().includes(term);
    return matchCat && matchTerm;
  });

  const countBadge = document.getElementById('nicNocCountBadge');
  if (countBadge) {
    countBadge.textContent = `${filtered.length} ข้อวินิจฉัย`;
  }

  if (filtered.length === 0) {
    listPane.innerHTML = `
      <div style="padding: 24px; text-align: center; color: var(--text-muted); font-size: 14px;">
        <i class="fa-solid fa-folder-open" style="font-size: 28px; margin-bottom: 8px; opacity: 0.5;"></i>
        <div>ไม่พบข้อวินิจฉัย NANDA-I ที่ตรงกับเงื่อนไข</div>
      </div>
    `;
    return;
  }

  filtered.forEach(item => {
    const card = document.createElement('div');
    const isSelected = activeNicNocItem && activeNicNocItem.code === item.code;
    card.className = `nicnoc-card-item ${isSelected ? 'active' : ''}`;
    card.setAttribute('data-code', item.code);
    card.onclick = () => selectNicNocDiagnosis(item.code);

    card.innerHTML = `
      <div class="nicnoc-card-top">
        <span class="nicnoc-code-badge"><i class="fa-solid fa-tag"></i> ${item.code}</span>
        <span class="nicnoc-domain-tag">${item.category}</span>
      </div>
      <div class="nicnoc-card-title">${item.name}</div>
      <div class="nicnoc-card-desc">${item.relatedTo}</div>
    `;

    listPane.appendChild(card);
  });
}

function selectNicNocDiagnosis(code) {
  const item = NIC_NOC_CATALOG.find(i => i.code === code);
  if (!item) return;

  activeNicNocItem = JSON.parse(JSON.stringify(item)); // Deep copy to allow local edit

  // Update card highlights
  const cards = document.querySelectorAll('.nicnoc-card-item');
  cards.forEach(c => {
    if (c.getAttribute('data-code') === code) {
      c.classList.add('active');
    } else {
      c.classList.remove('active');
    }
  });

  // Populate Details
  const titleEl = document.getElementById('nicNocActiveTitle');
  const codeEl = document.getElementById('nicNocActiveCode');
  const domainEl = document.getElementById('nicNocActiveDomain');
  const relatedEl = document.getElementById('nicNocActiveRelated');

  if (titleEl) titleEl.textContent = activeNicNocItem.name;
  if (codeEl) codeEl.textContent = `NANDA-I: ${activeNicNocItem.code}`;
  if (domainEl) domainEl.textContent = activeNicNocItem.domain;
  if (relatedEl) relatedEl.textContent = `ปัจจัยที่เกี่ยวข้อง: ${activeNicNocItem.relatedTo}`;

  const subjEl = document.getElementById('nicNocTxtSubj');
  const objEl = document.getElementById('nicNocTxtObj');
  const goalEl = document.getElementById('nicNocTxtGoal');
  const respEl = document.getElementById('nicNocTxtResp');

  if (subjEl) subjEl.value = activeNicNocItem.subjective;
  if (objEl) objEl.value = activeNicNocItem.objective;
  if (goalEl) goalEl.value = activeNicNocItem.goal;
  if (respEl) respEl.value = activeNicNocItem.response;

  renderNicNocActivities();
  updateNicNocDarPreview();
}

function renderNicNocActivities() {
  const container = document.getElementById('nicNocActivitiesList');
  if (!container || !activeNicNocItem) return;

  container.innerHTML = '';

  const visibleActs = activeNicNocItem.interventions.filter(a => {
    if (currentNicNocShift === 'all') return true;
    return a.shift === 'all' || a.shift === currentNicNocShift;
  });

  if (visibleActs.length === 0) {
    container.innerHTML = `<div style="color:var(--text-muted); font-size:13px; padding:8px 0;">ไม่มีกิจกรรมเฉพาะเวรนี้ (สามารถเลือกแท็บ 'ทั้งหมด')</div>`;
    return;
  }

  visibleActs.forEach((act, idx) => {
    const itemRow = document.createElement('label');
    itemRow.className = 'nicnoc-act-row';

    let shiftBadge = '';
    if (act.shift === 'morning') shiftBadge = '<span class="shift-badge morning">☀️ เช้า</span>';
    else if (act.shift === 'afternoon') shiftBadge = '<span class="shift-badge afternoon">⛅ บ่าย</span>';
    else if (act.shift === 'night') shiftBadge = '<span class="shift-badge night">🌙 ดึก</span>';
    else shiftBadge = '<span class="shift-badge all">ทุกเวร</span>';

    itemRow.innerHTML = `
      <input type="checkbox" ${act.checked ? 'checked' : ''} class="nicnoc-act-check">
      <div class="nicnoc-act-content">
        <span class="nicnoc-act-text">${act.text}</span>
        ${shiftBadge}
      </div>
    `;

    const chk = itemRow.querySelector('input');
    chk.onchange = () => {
      act.checked = chk.checked;
      updateNicNocDarPreview();
    };

    container.appendChild(itemRow);
  });
}

function buildNicNocDarText() {
  if (!activeNicNocItem) return '';

  const subj = (document.getElementById('nicNocTxtSubj')?.value || '').trim();
  const obj = (document.getElementById('nicNocTxtObj')?.value || '').trim();
  const goal = (document.getElementById('nicNocTxtGoal')?.value || '').trim();
  const resp = (document.getElementById('nicNocTxtResp')?.value || '').trim();

  const selectedActs = (activeNicNocItem.interventions || []).filter(a => a.checked);

  let dataParts = [];
  if (subj) dataParts.push(subj);
  if (obj) dataParts.push(obj);
  let dataStr = dataParts.join(" | ");

  let dar = `[FOCUS]: ${activeNicNocItem.name} (NANDA-I: ${activeNicNocItem.code})\r\n`;
  if (dataStr) {
    dar += `[D - DATA]: ${dataStr}\r\n`;
  }
  if (goal) {
    dar += `[G - GOAL / NOC]: ${goal}\r\n`;
  }

  let actStr = "";
  if (selectedActs.length > 0) {
    actStr = selectedActs.map(a => `- ${a.text}`).join("\r\n");
    dar += `[A - ACTION / NIC]:\r\n${actStr}\r\n`;
  } else {
    dar += `[A - ACTION / NIC]:\r\n- ให้การดูแลทางการพยาบาลตามแผนการรักษาและเฝ้าระวังอาการอย่างใกล้ชิด\r\n`;
  }

  if (resp) {
    dar += `[R - RESPONSE / NOC]: ${resp}`;
  }

  return dar;
}

function updateNicNocDarPreview() {
  const previewBox = document.getElementById('nicNocDarPreview');
  if (previewBox) {
    previewBox.value = buildNicNocDarText();
  }
}

function copyNicNocDar() {
  const text = buildNicNocDarText();
  if (!text) return;

  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(text).then(() => {
      showToast("📋 คัดลอกบันทึก Focus Charting (DAR) จากแผน NIC/NOC แล้ว");
    }).catch(() => fallbackCopy(text));
  } else {
    fallbackCopy(text);
  }
}

function fallbackCopy(text) {
  const ta = document.createElement('textarea');
  ta.value = text;
  document.body.appendChild(ta);
  ta.select();
  document.execCommand('copy');
  document.body.removeChild(ta);
  showToast("📋 คัดลอกบันทึก Focus Charting (DAR) แล้ว");
}

function insertNicNocToActiveBed() {
  const text = buildNicNocDarText();
  if (!text) return;

  const targetBed = activeNicNocTargetBed || 1;

  // If patient note modal is currently open for this bed, append to textarea
  const noteArea = document.getElementById('patientNote');
  if (typeof activeBedNumber !== 'undefined' && activeBedNumber === targetBed && noteArea) {
    const curVal = noteArea.value || '';
    noteArea.value = curVal ? (curVal.trim() + "\r\n\r\n" + text) : text;
    if (typeof handleNoteInput === 'function') handleNoteInput();
    closeNicNocModal();
    showToast(`🛏️ แทรกลงบันทึกเตียง ${String(targetBed).padStart(2, '0')} สำเร็จ`);
    return;
  }

  // Otherwise, use openBedModal or update directly in bedsData
  if (typeof openBedModal === 'function') {
    closeNicNocModal();
    openBedModal(targetBed);
    setTimeout(() => {
      const area = document.getElementById('patientNote');
      if (area) {
        const curVal = area.value || '';
        area.value = curVal ? (curVal.trim() + "\r\n\r\n" + text) : text;
        if (typeof handleNoteInput === 'function') handleNoteInput();
        showToast(`🛏️ แทรกลงบันทึกเตียง ${String(targetBed).padStart(2, '0')} เรียบร้อยแล้ว`);
      }
    }, 250);
  } else {
    copyNicNocDar();
  }
}

// Auto-initialize when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initNicNocModule);
} else {
  initNicNocModule();
}
