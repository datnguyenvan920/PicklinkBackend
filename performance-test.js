import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

// ====================================================================
// 1. CẤU HÌNH KIỂM THỬ TẢI (LOAD TEST OPTIONS)
// ====================================================================
export const options = {
  insecureSkipTLSVerify: true, // Bỏ qua kiểm tra chứng chỉ SSL nếu server dùng cert tự ký/staging
  stages: [
    { duration: '20s', target: 20 },  // Giai đoạn 1: Khởi động tăng dần lên 20 Virtual Users (VUs)
    { duration: '30s', target: 50 },  // Giai đoạn 2: Tải ổn định với 50 VUs
    { duration: '20s', target: 100 }, // Giai đoạn 3: Đẩy tải đỉnh (Spike) lên 100 VUs đồng thời
    { duration: '20s', target: 20 },  // Giai đoạn 4: Hạ nhiệt về 20 VUs
    { duration: '10s', target: 0 },   // Kết thúc: Về 0
  ],
  thresholds: {
    // Chỉ tiêu hiệu năng (SLA):
    'http_req_duration': ['p(95)<500', 'p(99)<1000'], // 95% request phải phản hồi < 500ms, 99% < 1000ms
    'http_req_failed': ['rate<0.02'],                  // Tỷ lệ lỗi mạng/kết nối phải dưới 2%
    'system_errors': ['rate<0.01'],                    // Tỷ lệ lỗi máy chủ (5xx) phải dưới 1%
    'read_venue_duration': ['p(95)<300'],              // Thời gian đọc danh sách sân < 300ms
    'booking_hold_duration': ['p(95)<600'],            // Thời gian giữ sân có lock DB < 600ms
  },
};

// ====================================================================
// 2. CÁC BIẾN & METRICS ĐO LƯỜNG TÙY BIẾN
// ====================================================================
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000'; // Có thể truyền qua CLI: k6 run -e BASE_URL=https://api.yourdomain.com

// Custom Metrics
const ReadVenueDuration = new Trend('read_venue_duration');
const BookingHoldDuration = new Trend('booking_hold_duration');
const SuccessfulHolds = new Counter('successful_booking_holds');
const ConflictHolds = new Counter('conflict_booking_holds');
const ErrorRate = new Rate('system_errors');

// Dữ liệu tài khoản test (Tự động đăng nhập lấy JWT token)
const TEST_USER = {
  email: __ENV.TEST_EMAIL || 'player1@picklink.vn',
  password: __ENV.TEST_PASSWORD || 'Password123!',
};

// ====================================================================
// 3. SETUP FUNCTION: Chạy 1 lần trước khi bắn tải để lấy Token JWT
// ====================================================================
export function setup() {
  console.log(`🚀 Bắt đầu kịch bản kiểm thử hiệu năng tại URL: ${BASE_URL}`);
  
  const loginPayload = JSON.stringify(TEST_USER);
  const headers = { 'Content-Type': 'application/json' };

  const res = http.post(`${BASE_URL}/api/auth/login`, loginPayload, { headers });

  if (res.status === 200) {
    try {
      const data = JSON.parse(res.body);
      const token = data.token || (data.data && data.data.token);
      console.log('✅ Đăng nhập lấy JWT Token thành công.');
      return { token: token };
    } catch (e) {
      console.warn('⚠️ Phản hồi đăng nhập không chứa Token hợp lệ, tiếp tục chế độ test Anonymous.');
    }
  } else {
    console.warn(`⚠️ Không đăng nhập được (HTTP ${res.status}). Hệ thống sẽ test các endpoint Public.`);
  }

  return { token: null };
}

// ====================================================================
// 4. MAIN VU SCRIPT: Luồng thao tác của người dùng mô phỏng
// ====================================================================
export default function (data) {
  const token = data.token;
  const authHeaders = {
    'Content-Type': 'application/json',
    ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
  };

  // ------------------------------------------------------------------
  // LUỒNG 1: Xem danh sách sân & Tìm kiếm (Đọc - 70% tải)
  // ------------------------------------------------------------------
  group('1. Browse Venues & Courts', function () {
    const startTime = Date.now();
    const res = http.get(`${BASE_URL}/api/player-bookings/venues?page=1&pageSize=10`);
    ReadVenueDuration.add(Date.now() - startTime);

    const isOk = check(res, {
      'Venues status is 200': (r) => r.status === 200,
      'Venues response has body': (r) => r.body && r.body.length > 0,
    });

    if (!isOk && res.status >= 500) {
      ErrorRate.add(1);
    } else {
      ErrorRate.add(0);
    }
  });

  sleep(0.5); // Nghỉ 0.5s giữa các thao tác

  // ------------------------------------------------------------------
  // LUỒNG 2: Kiểm tra lịch trống sân (Read Availability)
  // ------------------------------------------------------------------
  group('2. Check Court Availability', function () {
    const today = new Date().toISOString().split('T')[0];
    const res = http.get(`${BASE_URL}/api/player-bookings/venues/1/availability?date=${today}`);

    check(res, {
      'Availability status is 200 or 404': (r) => r.status === 200 || r.status === 404,
    });
  });

  sleep(0.5);

  // ------------------------------------------------------------------
  // LUỒNG 3: Thử thách Giữ chỗ & Khóa DB đồng thời (Write - Có Token)
  // ------------------------------------------------------------------
  if (token) {
    group('3. Concurrency Booking Hold', function () {
      const tomorrow = new Date();
      tomorrow.setDate(tomorrow.getDate() + 1);
      const dateString = tomorrow.toISOString().split('T')[0];

      // Thử giữ slot 08:00 - 09:00 tại sân ID 1
      const holdPayload = JSON.stringify({
        venueId: 1,
        date: dateString,
        slots: [
          {
            courtId: 1,
            startTime: '08:00:00',
            endTime: '09:00:00',
          },
        ],
      });

      const startTime = Date.now();
      // Khai báo responseCallback hoặc nhận 409/400 là expectedStatuses
      const res = http.post(`${BASE_URL}/api/player-bookings/hold`, holdPayload, {
        headers: authHeaders,
        responseCallback: http.expectedStatuses(200, 201, 400, 409),
      });
      BookingHoldDuration.add(Date.now() - startTime);

      if (res.status === 200 || res.status === 201) {
        SuccessfulHolds.add(1);
        check(res, { 'Booking Hold Success (200/201)': () => true });
      } else if (res.status === 409 || res.status === 400) {
        // 409 Conflict hoặc 400 (Slot đã có người giữ) là kết quả hợp lệ về mặt nghiệp vụ khi nhiều user cùng giữ 1 slot
        ConflictHolds.add(1);
        check(res, { 'Booking Slot Busy (Conflict 409/400)': () => true });
      } else if (res.status >= 500) {
        ErrorRate.add(1);
        check(res, { 'Server Error on Hold (5xx)': () => false });
      }
    });
  }

  sleep(1); // Thời gian suy nghĩ của người dùng trước lượt kế tiếp
}

// ====================================================================
// 5. TEARDOWN FUNCTION: Chạy 1 lần sau khi kết thúc đợt test
// ====================================================================
export function teardown() {
  console.log('🏁 Hoàn thành kiểm thử hiệu năng k6 cho PickLink Backend.');
}
