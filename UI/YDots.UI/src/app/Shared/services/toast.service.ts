import { Injectable, signal } from '@angular/core';
import { Toast } from '../models/toast.model';

@Injectable({
  providedIn:'root'
})
export class ToastService {

  toasts = signal<Toast[]>([]);

  private id = 0;

  show(
    title:string,
    message:string,
    type:'success'|'error'|'warning'|'info'='success',
    duration:number=4000
  ){

    const toast:Toast={

      id:++this.id,

      title,

      message,

      type

    };

    this.toasts.update(list=>[...list,toast]);

    setTimeout(()=>{

      this.remove(toast.id);

    },duration);

  }

  remove(id:number){

    this.toasts.update(list=>

      list.filter(x=>x.id!==id)

    );

  }

  // ============ Semantic convenience wrappers over show() ============
  // Same behaviour as show(), just without repeating the type literal at
  // every call site. Existing show() callers keep working unchanged.

  success(title:string, message:string, duration:number=4000){

    this.show(title, message, 'success', duration);

  }

  error(title:string, message:string, duration:number=5000){

    this.show(title, message, 'error', duration);

  }

  warning(title:string, message:string, duration:number=5000){

    this.show(title, message, 'warning', duration);

  }

  info(title:string, message:string, duration:number=4000){

    this.show(title, message, 'info', duration);

  }

}