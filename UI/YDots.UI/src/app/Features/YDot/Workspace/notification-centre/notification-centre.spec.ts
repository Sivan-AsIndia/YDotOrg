import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NotificationCentreComponent } from './notification-centre';

describe('NotificationCentreComponent', () => {
  let component: NotificationCentreComponent;
  let fixture: ComponentFixture<NotificationCentreComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationCentreComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationCentreComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
