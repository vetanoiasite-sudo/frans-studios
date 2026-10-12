import { createBrowserRouter, Outlet } from 'react-router'
import { AuthProvider } from '../context/AuthContext.jsx'
import { RouteError } from '../pages/RouteError.jsx'
import PublicLayout from '../layouts/PublicLayout.jsx'

// Each page is its own chunk. The public site never downloads admin/client code (and vice versa).
const page = (load) => () => load().then((m) => ({ Component: m.default }))

/** Only the private areas need to know who is signed in. */
function AuthRoot() {
  return (
    <AuthProvider>
      <Outlet />
    </AuthProvider>
  )
}

export const router = createBrowserRouter([
  {
    path: '/',
    Component: PublicLayout,
    errorElement: <RouteError />,
    children: [
      { index: true, lazy: page(() => import('../pages/public/HomePage.jsx')) },
      { path: 'portfolio', lazy: page(() => import('../pages/public/PortfolioPage.jsx')) },
      { path: 'portfolio/:slug', lazy: page(() => import('../pages/public/GalleryPage.jsx')) },
      { path: 'films', lazy: page(() => import('../pages/public/FilmsPage.jsx')) },
      { path: 'about', lazy: page(() => import('../pages/public/AboutPage.jsx')) },
      { path: 'packages', lazy: page(() => import('../pages/public/PackagesPage.jsx')) },
      { path: 'contact', lazy: page(() => import('../pages/public/ContactPage.jsx')) },
      { path: '*', lazy: page(() => import('../pages/public/NotFoundPage.jsx')) },
    ],
  },
  {
    Component: AuthRoot,
    errorElement: <RouteError />,
    children: [
      { path: '/client/login', lazy: page(() => import('../pages/client/ClientLoginPage.jsx')) },
      {
        path: '/client',
        lazy: page(() => import('../layouts/ClientLayout.jsx')),
        children: [
          { index: true, lazy: page(() => import('../pages/client/ClientDashboardPage.jsx')) },
          { path: 'albums/:id', lazy: page(() => import('../pages/client/ClientAlbumPage.jsx')) },
        ],
      },
      { path: '/admin/login', lazy: page(() => import('../pages/admin/AdminLoginPage.jsx')) },
      {
        path: '/admin',
        lazy: page(() => import('../layouts/AdminLayout.jsx')),
        children: [
          { index: true, lazy: page(() => import('../pages/admin/DashboardPage.jsx')) },
          { path: 'clients', lazy: page(() => import('../pages/admin/ClientsPage.jsx')) },
          { path: 'clients/new', lazy: page(() => import('../pages/admin/ClientNewPage.jsx')) },
          { path: 'clients/:id', lazy: page(() => import('../pages/admin/ClientDetailPage.jsx')) },
          { path: 'albums', lazy: page(() => import('../pages/admin/AlbumsPage.jsx')) },
          { path: 'albums/new', lazy: page(() => import('../pages/admin/AlbumEditPage.jsx')) },
          { path: 'albums/:id', lazy: page(() => import('../pages/admin/AlbumEditPage.jsx')) },
          { path: 'portfolio', lazy: page(() => import('../pages/admin/GalleriesPage.jsx')) },
          { path: 'portfolio/:id', lazy: page(() => import('../pages/admin/GalleryEditPage.jsx')) },
          { path: 'films', lazy: page(() => import('../pages/admin/FilmsAdminPage.jsx')) },
          { path: 'packages', lazy: page(() => import('../pages/admin/PackagesAdminPage.jsx')) },
          { path: 'page-covers', lazy: page(() => import('../pages/admin/PageCoversPage.jsx')) },
          { path: 'inquiries', lazy: page(() => import('../pages/admin/InquiriesPage.jsx')) },
          { path: 'drive', lazy: page(() => import('../pages/admin/DrivePage.jsx')) },
          { path: 'google-drive', lazy: page(() => import('../pages/admin/GoogleDrivePage.jsx')) },
          { path: 'settings', lazy: page(() => import('../pages/admin/SettingsPage.jsx')) },
        ],
      },
    ],
  },
])
