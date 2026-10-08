// Loads Swagger UI for the OpenAPI document ASP.NET Core generates.
// Kept in its own file because the Content-Security-Policy forbids inline scripts.
window.addEventListener('DOMContentLoaded', () => {
  window.SwaggerUIBundle({
    url: '/openapi/v1.json',
    dom_id: '#swagger-ui',
    deepLinking: true,
    tryItOutEnabled: true,
  });
});
