/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{html,ts,scss}'],
  theme: {
    extend: {
      colors: {
        primary: { DEFAULT: '#6366f1', 50: '#eef2ff', 500: '#6366f1', 600: '#4f46e5', 700: '#4338ca' },
        success: '#22c55e',
        warning: '#f59e0b',
        danger: '#ef4444',
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
      },
      borderRadius: { xl: '12px', '2xl': '16px' },
    },
  },
  plugins: [
    require('@tailwindcss/forms'),
  ],
  // Disable preflight — Angular Material handles base styles
  corePlugins: {
    preflight: false,
  },
};
