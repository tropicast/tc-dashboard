module.exports = {
    ci: {
        collect: {
            startServerCommand: 'npm run dev -- --host localhost',
            startServerReadyPattern: 'Local:',
            startServerReadyTimeout: 30000,
            url: ['http://localhost:5173/login', 'http://localhost:5173/signup'],
            numberOfRuns: 1,
        },
        assert: {
            assertions: {
                'categories:accessibility': ['error', { minScore: 0.95 }],
            },
        },
    },
};
