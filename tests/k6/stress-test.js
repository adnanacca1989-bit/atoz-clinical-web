import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'https://atoz-clinical.onrender.com';

export const options = {
  scenarios: {
    stress: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 2000,
      stages: [
        { duration: '2m', target: 50 },
        { duration: '2m', target: 100 },
        { duration: '2m', target: 200 },
        { duration: '2m', target: 400 },
        { duration: '2m', target: 800 },
        { duration: '2m', target: 1200 },
        { duration: '2m', target: 1600 }
      ]
    }
  },
  thresholds: {
    http_req_failed: ['rate<0.5']
  }
};

export default function () {
  const endpoints = [
    '/health',
    '/Account/Login',
    '/Portal/Login'
  ];

  const path = endpoints[Math.floor(Math.random() * endpoints.length)];
  const res = http.get(`${BASE_URL}${path}`, { tags: { name: path } });

  check(res, {
    'not server error': (r) => r.status < 500
  });

  sleep(0.2);
}
