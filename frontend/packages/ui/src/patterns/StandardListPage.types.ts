import type { ReactNode } from 'react';

export interface StandardListPageProps {
  title: string;
  eyebrow?: string;
  description?: string;
  headerActions?: ReactNode;
  filterBar?: ReactNode;
  content: ReactNode;
  footerLeft?: ReactNode;
  footerRight?: ReactNode;
  advancedFilterTrigger?: ReactNode;
  advancedFilterTitle?: string;
  advancedFilterOpen?: boolean;
  advancedFilterContent?: ReactNode;
  onAdvancedFilterClose?: () => void;
}
