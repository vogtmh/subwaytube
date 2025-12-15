// Polyfill for fetch using Windows.Web.Http.HttpClient
// This bypasses CORS restrictions in UWP apps with the internetClient capability.

async function uwpFetch(input, init = {}) {
    let url = input;
    if (input instanceof Request) {
        url = input.url;
    }

    const method = init.method || 'GET';
    const headers = init.headers || {};
    const body = init.body || null;

    const httpClient = new Windows.Web.Http.HttpClient();

    // Add headers
    for (const [key, value] of Object.entries(headers)) {
        // Some headers might need special handling or are restricted
        try {
            if (key.toLowerCase() === 'content-type') {
                // Content-Type is set on the content, not the request headers
                continue;
            }
            httpClient.defaultRequestHeaders.tryAppendWithoutValidation(key, value);
        } catch (e) {
            console.warn(`Failed to append header ${key}: ${e.message}`);
        }
    }

    let httpContent = null;
    if (body) {
        if (typeof body === 'string') {
            httpContent = new Windows.Web.Http.HttpStringContent(body);
        } else if (body instanceof Blob) {
            // Handle Blob/File if needed, simplified for string/json
            // For now, assuming string/JSON for API interactions
            // If binary is needed, we'd need to use HttpBufferContent
            console.warn("Blob body support not fully implemented in uwpFetch yet");
        }

        // Set Content-Type if present
        if (headers['Content-Type']) {
            httpContent.headers.contentType = new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue(headers['Content-Type']);
        } else if (headers['content-type']) {
            httpContent.headers.contentType = new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue(headers['content-type']);
        }
    }

    let uri;
    try {
        uri = new Windows.Foundation.Uri(url);
    } catch (e) {
        throw new TypeError(`Invalid URL: ${url}`);
    }

    let response;
    try {
        if (method === 'GET') {
            response = await httpClient.getAsync(uri);
        } else if (method === 'POST') {
            response = await httpClient.postAsync(uri, httpContent);
        } else {
            // Implement other methods as needed
            throw new Error(`Method ${method} not implemented in uwpFetch yet`);
        }
    } catch (e) {
        throw new TypeError(`Network request failed: ${e.message}`);
    }

    // Convert UWP response to standard Response object
    const responseBodyText = await response.content.readAsStringAsync();

    // Create a Headers object
    const responseHeaders = new Headers();
    // UWP headers are iterable
    for (const header of response.headers) {
        responseHeaders.append(header.key, header.value);
    }
    for (const header of response.content.headers) {
        responseHeaders.append(header.key, header.value);
    }

    return new Response(responseBodyText, {
        status: response.statusCode,
        statusText: response.reasonPhrase,
        headers: responseHeaders
    });
}

// Export it or attach to window if needed, but InnerTube allows passing a fetch implementation
window.uwpFetch = uwpFetch;
