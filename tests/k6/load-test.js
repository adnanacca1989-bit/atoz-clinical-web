import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const errorRate = new Rate('errors');
const healthDuration = new Trend('health_duration', true);
const loginDuration = new Trend('login_page_duration', true);
const dashboardDuration = new Trend('dashboard_duration', true);

const BASE_URL = __ENV.BASE_URL || 'https://atoz-clinical.onrender.com';
const USERNAME = __ENV.USERNAME || 'vendor';
const PASSWORD = __ENV.PASSWORD || '';
const HEALTH_TOKEN = __ENV.HEALTH_TOKEN || '';

export const options = {
  scenarios: {
    load: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '1m', target: Number(__ENV.VUS || 100) },
        { duration: '3m', target: Number(__ENV.VUS || 100) },
        { duration: '1m', target: 0 }
      ],
      gracefulRampDown: '30s'
    }
  },
  thresholds: {
    http_req_duration: ['p(95)<3000'],
    errors: ['rate<0.05']
  }
};

export default function () {
  const healthHeaders = HEALTH_TOKEN ? { 'X-Health-Token': HEALTH_TOKEN } : {};
  const healthRes = http.get(`${BASE_URL}/health`, { headers: healthHeaders, tags: { name: 'health' } });
  healthDuration.add(healthRes.timings.duration);
  check(healthRes, { 'health status 200': (r) => r.status === 200 }) || errorRate.add(1);

  const loginRes = http.get(`${BASE_URL}/Account/Login`, { tags: { name: 'login_page' } });
  loginDuration.add(loginRes.timings.duration);
  check(loginRes, { 'login page 200': (r) => r.status === 200 }) || errorRate.add(1);

  if (PASSWORD) {
    const payload = {
      'Input.Username': USERNAME,
      'Input.Password': PASSWORD
    };
    const authRes = http.post(`${BASE_URL}/Account/Login`, payload, { tags: { name: 'login_post' } });
    check(authRes, {
      'login post not 500': (r) => r.status !== 500
    }) || errorRate.add(1);

    const dashRes = http.get(`${BASE_URL}/Dashboard`, { tags: { name: 'dashboard' } });
    dashboardDuration.add(dashRes.timings.duration);
    check(dashRes, {
      'dashboard reachable': (r) => r.status === 200 || r.status === 302
    }) || errorRate.add(1);
  }

  sleep(1);
}

export function handleSummary(data) {
  return {
  stdout: JSON.stringify({
    scenario: `load-${__ENV.VUS || 100}-vus`,
    metrics: {
      http_req_duration_avg: data.metrics.http_req_duration?.values?.avg,
      http_req_duration_p95: data.metrics.http_req_duration?.values?.['p(95)'],
      http_reqs: data.metrics.http_reqs?.values?.count,
      http_req_failed: data.metrics.http_req_failed?.values?.rate,
      errors: data.metrics.errors?.values?.rate,
      vus_max: data.metrics.vus_max?.values?.max
    }
  }, null, 2)
  };
}
