export type ToastType =
  | 'success'
  | 'error'
  | 'warning'
  | 'info';

export interface Toast {

  id: number;

  title: string;

  message: string;

  type: ToastType;

}